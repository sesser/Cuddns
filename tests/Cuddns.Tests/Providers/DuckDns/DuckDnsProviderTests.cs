using System.Net;
using System.Net.Http;
using Cuddns.Providers;
using Cuddns.Providers.DuckDns;
using FluentAssertions;

namespace Cuddns.Tests.Providers.DuckDns;

public class DuckDnsProviderTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(respond(request));
        }
    }

    private static DuckDnsProviderConfig BuildConfig(params string[] records)
    {
        return new DuckDnsProviderConfig { Token = "test-token", Records = [.. records] };
    }

    [Fact]
    public void ManagedRecords_ReflectsConfiguredRecords()
    {
        var config = BuildConfig("home.duckdns.org", "vpn.duckdns.org");
        var provider = new DuckDnsProvider(new HttpClient(), config);

        provider.ManagedRecords.Should().BeEquivalentTo(
        [
            new ManagedRecord("home.duckdns.org", 60),
            new ManagedRecord("vpn.duckdns.org", 60),
        ]);
    }

    [Fact]
    public async Task UpsertRecordAsync_OkResponse_SendsSubdomainTokenAndIp()
    {
        var handler = new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("OK") });
        using var httpClient = new HttpClient(handler);
        var provider = new DuckDnsProvider(httpClient, BuildConfig("home.duckdns.org"));

        await provider.UpsertRecordAsync(provider.ManagedRecords[0], "203.0.113.10", CancellationToken.None);

        handler.LastRequestUri.Should().NotBeNull();
        var query = handler.LastRequestUri!.Query;
        query.Should().Contain("domains=home");
        query.Should().Contain("token=test-token");
        query.Should().Contain("ip=203.0.113.10");
    }

    [Fact]
    public async Task UpsertRecordAsync_KoResponse_Throws()
    {
        using var httpClient = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("KO") }));
        var provider = new DuckDnsProvider(httpClient, BuildConfig("home.duckdns.org"));

        var act = () => provider.UpsertRecordAsync(provider.ManagedRecords[0], "203.0.113.10", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
