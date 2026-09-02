using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OtpNet;

namespace Cuddns.Providers.Miab;

public sealed class MiabDnsProvider : IDnsProvider, IDeletableDnsProvider
{
    // MiaB's custom DNS API doesn't expose a TTL knob; this is informational only, same as
    // DuckDNS/No-IP.
    private const int MiabTtlSeconds = 300;

    private readonly HttpClient _httpClient;
    private readonly string _hostname;
    private readonly AuthenticationHeaderValue _authHeader;

    // Set only when the admin account has 2FA turned on. MiaB's own TOTP setup uses
    // pyotp's defaults (SHA1, 6 digits, 30s step) — matched explicitly here rather than
    // relying on this library's defaults staying the same across versions.
    private readonly Totp? _totp;

    // Cached session credential from a prior /login call — see GetAuthHeaderAsync. MiaB
    // rejects a repeated TOTP code as a replay, so a fresh code can only be spent once per
    // process; logging in once and reusing the resulting session key sidesteps that entirely
    // instead of needing a new TOTP code for every record update.
    private AuthenticationHeaderValue? _sessionAuthHeader;

    public MiabDnsProvider(HttpClient httpClient, MiabProviderConfig config)
    {
        _httpClient = httpClient;
        _hostname = config.Hostname!;
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.Username}:{config.Password}"));
        _authHeader = new AuthenticationHeaderValue("Basic", credentials);
        _totp = string.IsNullOrWhiteSpace(config.TotpSecret)
            ? null
            : new Totp(Base32Encoding.ToBytes(config.TotpSecret), step: 30, mode: OtpHashMode.Sha1, totpSize: 6);
        PruneRemovedRecords = config.PruneRemovedRecords;
        ManagedRecords = config.Records.Select(record =>
        {
            var (name, type) = RecordSpec.Parse(record);
            return new ManagedRecord(name, MiabTtlSeconds, type);
        }).ToList();
    }

    public string ProviderType => "miab";

    public IReadOnlyList<ManagedRecord> ManagedRecords { get; }

    public bool PruneRemovedRecords { get; }

    public Task UpsertRecordAsync(ManagedRecord record, string ip, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Put, record, ip, cancellationToken);

    // A single MiaB provider instance manages one box — there's no per-record zone to scope
    // to like Route53/Cloudflare, so the box's own hostname doubles as the (only) scope value.
    public string GetScope(ManagedRecord record) => _hostname;

    public bool OwnsScope(string scope) => scope == _hostname;

    public Task DeleteRecordAsync(ManagedRecord record, string scope, string lastKnownIp, CancellationToken cancellationToken) =>
        // Scoping the delete to lastKnownIp (rather than an empty body, which deletes every
        // record for this qname/rtype) leaves alone any other value manually added for the
        // same hostname, e.g. a round-robin entry Cuddns doesn't manage.
        SendAsync(HttpMethod.Delete, record, lastKnownIp, cancellationToken);

    private async Task SendAsync(HttpMethod method, ManagedRecord record, string value, CancellationToken cancellationToken)
    {
        var rtype = record.Type == RecordType.AAAA ? "aaaa" : "a";
        var url = $"https://{_hostname}/admin/dns/custom/{Uri.EscapeDataString(record.Name)}/{rtype}";

        using var response = await SendAuthenticatedAsync(method, url, value, cancellationToken);
        var result = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();

        // MiaB's success body is a free-form confirmation message (e.g. "updated DNS:
        // example.com"), not a fixed string — the HTTP status is the only reliable signal.
        if (!response.IsSuccessStatusCode)
        {
            var action = method == HttpMethod.Delete ? "delete" : "update";
            throw new InvalidOperationException($"MiaB {action} for {record.Name} failed: '{Truncate(result)}'");
        }
    }

    private async Task<HttpResponseMessage> SendAuthenticatedAsync(
        HttpMethod method, string url, string body, CancellationToken cancellationToken)
    {
        var authHeader = await GetAuthHeaderAsync(cancellationToken);
        var response = await SendOnceAsync(method, url, body, authHeader, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized && _totp is not null)
        {
            // The cached session likely expired or was invalidated server-side (MiaB keeps
            // sessions in memory, so they don't survive a daemon restart) — log in again for
            // a fresh one and retry exactly once.
            response.Dispose();
            _sessionAuthHeader = null;
            authHeader = await GetAuthHeaderAsync(cancellationToken);
            response = await SendOnceAsync(method, url, body, authHeader, cancellationToken);
        }

        return response;
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        HttpMethod method, string url, string body, AuthenticationHeaderValue authHeader, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url)
        {
            // The API defaults A/AAAA to the caller's own remote address when the request
            // body is empty — send the value explicitly instead, since the box isn't
            // necessarily what sees the same public address Cuddns did.
            Content = new StringContent(body),
        };
        request.Headers.Authorization = authHeader;
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private async Task<AuthenticationHeaderValue> GetAuthHeaderAsync(CancellationToken cancellationToken)
    {
        if (_totp is null)
        {
            return _authHeader;
        }

        _sessionAuthHeader ??= await LoginAsync(cancellationToken);
        return _sessionAuthHeader;
    }

    private async Task<AuthenticationHeaderValue> LoginAsync(CancellationToken cancellationToken)
    {
        // The public API is reverse-proxied under /admin/ (nginx strips that prefix before
        // forwarding to the management daemon) — same as the /admin/dns/custom/... path
        // used elsewhere in this file; a bare /login never reaches the daemon at all.
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{_hostname}/admin/login");
        request.Headers.Authorization = _authHeader;
        // Spent once here rather than on every DNS call — MiaB rejects a TOTP code reused
        // within the same 30s window as a replay, which a per-request code would hit as
        // soon as two records update in quick succession.
        request.Headers.Add("x-auth-token", _totp!.ComputeTotp());

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);

        MiabLoginResponse? login;
        try
        {
            login = JsonSerializer.Deserialize<MiabLoginResponse>(rawBody);
        }
        catch (JsonException)
        {
            // Not JSON at all — e.g. an HTML error/maintenance page from a proxy in front of
            // the box. Surface the raw body rather than a raw deserialization stack trace, so
            // a wrong URL or an outage is obvious from the log line instead of a JsonException.
            login = null;
        }

        if (login is null || login.Status != "ok" || string.IsNullOrEmpty(login.ApiKey))
        {
            throw new InvalidOperationException(
                $"MiaB login failed: '{Truncate(login?.Reason ?? login?.Status ?? rawBody)}'");
        }

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{login.ApiKey}:"));
        return new AuthenticationHeaderValue("Basic", credentials);
    }

    private static string Truncate(string value) => value.Length <= 200 ? value : value[..200] + "...";
}
