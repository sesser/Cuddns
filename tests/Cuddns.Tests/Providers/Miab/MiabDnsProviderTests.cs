using System.Net;
using System.Net.Http;
using Cuddns.Providers;
using Cuddns.Providers.Miab;
using FluentAssertions;

namespace Cuddns.Tests.Providers.Miab;

public class MiabDnsProviderTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request, LastBody);
        }
    }

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
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("OK") });
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
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("OK") });
        using var httpClient = new HttpClient(handler);
        var provider = new MiabDnsProvider(httpClient, BuildConfig("vpn.example.com:aaaa"));

        await provider.UpsertRecordAsync(provider.ManagedRecords[0], "2001:db8::1", CancellationToken.None);

        handler.LastRequest!.RequestUri!.ToString().Should().Be("https://box.example.com/admin/dns/custom/vpn.example.com/aaaa");
        handler.LastBody.Should().Be("2001:db8::1");
    }

    [Fact]
    public async Task UpsertRecordAsync_NonOkBody_Throws()
    {
        using var httpClient = new HttpClient(new StubHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Something isn't right.") }));
        var provider = new MiabDnsProvider(httpClient, BuildConfig("home.example.com"));

        var act = () => provider.UpsertRecordAsync(provider.ManagedRecords[0], "203.0.113.10", CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Something isn't right.*");
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
}
