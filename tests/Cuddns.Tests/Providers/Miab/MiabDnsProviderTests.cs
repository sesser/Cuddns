using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using Cuddns.Providers;
using Cuddns.Providers.Miab;
using FluentAssertions;
using OtpNet;

namespace Cuddns.Tests.Providers.Miab;

public class MiabDnsProviderTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string?> Bodies { get; } = [];

        public HttpRequestMessage? LastRequest => Requests.Count == 0 ? null : Requests[^1];

        public string? LastBody => Bodies.Count == 0 ? null : Bodies[^1];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(request);
            Bodies.Add(body);
            return respond(request, body);
        }
    }

    private const string TotpSecret = "JBSWY3DPEHPK3PXP";

    private static MiabProviderConfig BuildConfig(params string[] records)
    {
        return new MiabProviderConfig
        {
            Hostname = "box.example.com",
            Username = "admin@example.com",
            Password = "test-pass",
            Records = [.. records],
        };
    }

    private static HttpResponseMessage OkResponse(string message = "updated DNS: box.example.com") =>
        new(HttpStatusCode.OK) { Content = new StringContent(message) };

    private static HttpResponseMessage LoginResponse(string apiKey) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { status = "ok", email = "admin@example.com", privileges = new[] { "admin" }, api_key = apiKey }),
        };

    private static HttpResponseMessage LoginFailureResponse(string status, string reason) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(new { status, reason }) };

    [Fact]
    public void ManagedRecords_ReflectsConfiguredRecords()
    {
        var config = BuildConfig("home.example.com", "vpn.example.com");
        var provider = new MiabDnsProvider(new HttpClient(), config);

        provider.ManagedRecords.Should().BeEquivalentTo(
        [
            new ManagedRecord("home.example.com", 300),
            new ManagedRecord("vpn.example.com", 300),
        ]);
    }

    [Fact]
    public void ManagedRecords_AaaaSuffix_ParsesNameAndType()
    {
        var config = BuildConfig("vpn.example.com:aaaa");
        var provider = new MiabDnsProvider(new HttpClient(), config);

        provider.ManagedRecords.Should().BeEquivalentTo(
        [
            new ManagedRecord("vpn.example.com", 300, RecordType.AAAA),
        ]);
    }

    [Fact]
    public async Task UpsertRecordAsync_GoodResponse_SendsPutWithExplicitIpAndAuth()
    {
        var handler = new StubHandler((_, _) => OkResponse());
        using var httpClient = new HttpClient(handler);
        var provider = new MiabDnsProvider(httpClient, BuildConfig("home.example.com"));

        await provider.UpsertRecordAsync(provider.ManagedRecords[0], "203.0.113.10", CancellationToken.None);

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Put);
        handler.LastRequest.RequestUri!.ToString().Should().Be("https://box.example.com/admin/dns/custom/home.example.com/a");
        handler.LastRequest.Headers.Authorization.Should().NotBeNull();
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Basic");
        handler.LastBody.Should().Be("203.0.113.10");
    }

    [Fact]
    public async Task UpsertRecordAsync_AaaaRecord_UsesAaaaRtypeAndSendsIpv6Body()
    {
        var handler = new StubHandler((_, _) => OkResponse());
        using var httpClient = new HttpClient(handler);
        var provider = new MiabDnsProvider(httpClient, BuildConfig("vpn.example.com:aaaa"));

        await provider.UpsertRecordAsync(provider.ManagedRecords[0], "2001:db8::1", CancellationToken.None);

        handler.LastRequest!.RequestUri!.ToString().Should().Be("https://box.example.com/admin/dns/custom/vpn.example.com/aaaa");
        handler.LastBody.Should().Be("2001:db8::1");
    }

    [Fact]
    public async Task UpsertRecordAsync_SuccessStatusWithFreeFormBody_DoesNotThrow()
    {
        // MiaB's real success response is a human-readable confirmation like "updated DNS:
        // example.com", not a fixed string — this is a regression test for a bug where
        // Cuddns required the body to equal "OK" and reported a successful update as failed.
        using var httpClient = new HttpClient(new StubHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Something completely different") }));
        var provider = new MiabDnsProvider(httpClient, BuildConfig("home.example.com"));

        var act = () => provider.UpsertRecordAsync(provider.ManagedRecords[0], "203.0.113.10", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task UpsertRecordAsync_ErrorStatusCode_ThrowsWithBodyMessage()
    {
        using var httpClient = new HttpClient(new StubHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("Invalid input.") }));
        var provider = new MiabDnsProvider(httpClient, BuildConfig("home.example.com"));

        var act = () => provider.UpsertRecordAsync(provider.ManagedRecords[0], "203.0.113.10", CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Invalid input.*");
    }

    [Fact]
    public void OwnsScope_MatchesConfiguredHostname()
    {
        var provider = new MiabDnsProvider(new HttpClient(), BuildConfig("home.example.com"));

        provider.OwnsScope("box.example.com").Should().BeTrue();
        provider.OwnsScope("other-box.example.com").Should().BeFalse();
    }

    [Fact]
    public void GetScope_ReturnsConfiguredHostname()
    {
        var provider = new MiabDnsProvider(new HttpClient(), BuildConfig("home.example.com"));

        provider.GetScope(provider.ManagedRecords[0]).Should().Be("box.example.com");
    }

    [Fact]
    public async Task DeleteRecordAsync_SendsDeleteWithLastKnownIpAsBody()
    {
        var handler = new StubHandler((_, _) => OkResponse());
        using var httpClient = new HttpClient(handler);
        var provider = new MiabDnsProvider(httpClient, BuildConfig("home.example.com"));
        var removedRecord = new ManagedRecord("gone.example.com", 0);

        await provider.DeleteRecordAsync(removedRecord, "box.example.com", "203.0.113.10", CancellationToken.None);

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Delete);
        handler.LastRequest.RequestUri!.ToString().Should().Be("https://box.example.com/admin/dns/custom/gone.example.com/a");
        handler.LastBody.Should().Be("203.0.113.10");
    }

    [Fact]
    public async Task DeleteRecordAsync_AaaaRecord_UsesAaaaRtype()
    {
        var handler = new StubHandler((_, _) => OkResponse());
        using var httpClient = new HttpClient(handler);
        var provider = new MiabDnsProvider(httpClient, BuildConfig("home.example.com"));
        var removedRecord = new ManagedRecord("gone.example.com", 0, RecordType.AAAA);

        await provider.DeleteRecordAsync(removedRecord, "box.example.com", "2001:db8::1", CancellationToken.None);

        handler.LastRequest!.RequestUri!.ToString().Should().Be("https://box.example.com/admin/dns/custom/gone.example.com/aaaa");
    }

    [Fact]
    public async Task DeleteRecordAsync_ApiReturnsFailure_ThrowsWithErrorMessage()
    {
        using var httpClient = new HttpClient(new StubHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("boom") }));
        var provider = new MiabDnsProvider(httpClient, BuildConfig("home.example.com"));
        var removedRecord = new ManagedRecord("gone.example.com", 0);

        var act = () => provider.DeleteRecordAsync(removedRecord, "box.example.com", "203.0.113.10", CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*boom*");
    }

    [Fact]
    public async Task UpsertRecordAsync_NoTotpSecretConfigured_SkipsLoginAndUsesBasicAuthDirectly()
    {
        var handler = new StubHandler((_, _) => OkResponse());
        using var httpClient = new HttpClient(handler);
        var provider = new MiabDnsProvider(httpClient, BuildConfig("home.example.com"));

        await provider.UpsertRecordAsync(provider.ManagedRecords[0], "203.0.113.10", CancellationToken.None);

        handler.Requests.Should().ContainSingle();
        handler.LastRequest!.Headers.Contains("x-auth-token").Should().BeFalse();
    }

    [Fact]
    public async Task UpsertRecordAsync_TotpSecretConfigured_LogsInOnceAndUsesSessionApiKeyForDnsCall()
    {
        var config = BuildConfig("home.example.com");
        config.TotpSecret = TotpSecret;
        var expectedTotpCode = new Totp(Base32Encoding.ToBytes(TotpSecret)).ComputeTotp();
        var handler = new StubHandler((request, _) =>
            request.RequestUri!.AbsolutePath == "/login" ? LoginResponse("session-api-key") : OkResponse());
        using var httpClient = new HttpClient(handler);
        var provider = new MiabDnsProvider(httpClient, config);

        await provider.UpsertRecordAsync(provider.ManagedRecords[0], "203.0.113.10", CancellationToken.None);

        handler.Requests.Should().HaveCount(2);

        var loginRequest = handler.Requests[0];
        loginRequest.Method.Should().Be(HttpMethod.Post);
        loginRequest.RequestUri!.ToString().Should().Be("https://box.example.com/login");
        loginRequest.Headers.Authorization!.Scheme.Should().Be("Basic");
        loginRequest.Headers.GetValues("x-auth-token").Should().ContainSingle().Which.Should().Be(expectedTotpCode);

        var dnsRequest = handler.Requests[1];
        dnsRequest.Method.Should().Be(HttpMethod.Put);
        dnsRequest.Headers.Contains("x-auth-token").Should().BeFalse();
        dnsRequest.Headers.Authorization!.Scheme.Should().Be("Basic");
        var decodedCredentials = Encoding.UTF8.GetString(Convert.FromBase64String(dnsRequest.Headers.Authorization!.Parameter!));
        decodedCredentials.Should().Be("session-api-key:");
    }

    [Fact]
    public async Task TwoSequentialUpdates_TotpSecretConfigured_ReusesSessionWithoutLoggingInAgain()
    {
        var config = BuildConfig("a.example.com", "b.example.com");
        config.TotpSecret = TotpSecret;
        var handler = new StubHandler((request, _) =>
            request.RequestUri!.AbsolutePath == "/login" ? LoginResponse("session-api-key") : OkResponse());
        using var httpClient = new HttpClient(handler);
        var provider = new MiabDnsProvider(httpClient, config);

        await provider.UpsertRecordAsync(provider.ManagedRecords[0], "203.0.113.10", CancellationToken.None);
        await provider.UpsertRecordAsync(provider.ManagedRecords[1], "203.0.113.10", CancellationToken.None);

        handler.Requests.Should().HaveCount(3);
        handler.Requests.Count(r => r.RequestUri!.AbsolutePath == "/login").Should().Be(1);
    }

    [Fact]
    public async Task UpsertRecordAsync_DnsCallReturns401_LogsInAgainAndRetriesSuccessfully()
    {
        var config = BuildConfig("home.example.com");
        config.TotpSecret = TotpSecret;
        var dnsCallCount = 0;
        var handler = new StubHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/login")
            {
                return LoginResponse("session-api-key");
            }

            dnsCallCount++;
            return dnsCallCount == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("Incorrect email address or password.") }
                : OkResponse();
        });
        using var httpClient = new HttpClient(handler);
        var provider = new MiabDnsProvider(httpClient, config);

        var act = () => provider.UpsertRecordAsync(provider.ManagedRecords[0], "203.0.113.10", CancellationToken.None);

        await act.Should().NotThrowAsync();
        dnsCallCount.Should().Be(2);
        handler.Requests.Count(r => r.RequestUri!.AbsolutePath == "/login").Should().Be(2);
    }

    [Fact]
    public async Task UpsertRecordAsync_LoginFails_ThrowsWithReason()
    {
        var config = BuildConfig("home.example.com");
        config.TotpSecret = TotpSecret;
        using var httpClient = new HttpClient(new StubHandler(
            (_, _) => LoginFailureResponse("invalid", "Incorrect email address or password.")));
        var provider = new MiabDnsProvider(httpClient, config);

        var act = () => provider.UpsertRecordAsync(provider.ManagedRecords[0], "203.0.113.10", CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Incorrect email address or password.*");
    }
}
