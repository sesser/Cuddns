using System.Net.Http.Headers;
using System.Text;
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

        using var request = new HttpRequestMessage(method, url)
        {
            // The API defaults A/AAAA to the caller's own remote address when the request
            // body is empty — send the value explicitly instead, since the box isn't
            // necessarily what sees the same public address Cuddns did.
            Content = new StringContent(value),
        };
        request.Headers.Authorization = _authHeader;
        if (_totp is not null)
        {
            // MiaB rejects every request from a 2FA-enabled account with "missing-totp-token"
            // unless the current code is also sent this way — Basic auth alone isn't enough.
            request.Headers.Add("x-auth-token", _totp.ComputeTotp());
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var result = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();

        if (!response.IsSuccessStatusCode || !result.Equals("OK", StringComparison.Ordinal))
        {
            var action = method == HttpMethod.Delete ? "delete" : "update";
            throw new InvalidOperationException($"MiaB {action} for {record.Name} failed: '{Truncate(result)}'");
        }
    }

    private static string Truncate(string value) => value.Length <= 200 ? value : value[..200] + "...";
}
