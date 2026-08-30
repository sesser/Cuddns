using System.Net;
using System.Net.Http;
using Cuddns.Providers;
using Cuddns.Providers.NoIp;
using FluentAssertions;

namespace Cuddns.Tests.Providers.NoIp;

public class NoIpProviderTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }

    private static NoIpProviderConfig BuildConfig(params string[] records)
    {
        return new NoIpProviderConfig { Username = "test-user", Password = "test-pass", Records = [.. records] };
    }

    [Fact]
    public void ManagedRecords_ReflectsConfiguredRecords()
    {
        var config = BuildConfig("home.example.com", "vpn.example.com");
        var provider = new NoIpProvider(new HttpClient(), config);

        provider.ManagedRecords.Should().BeEquivalentTo(
        [
            new ManagedRecord("home.example.com", 300),
            new ManagedRecord("vpn.example.com", 300),
        ]);
    }

    [Fact]
    public async Task UpsertRecordAsync_GoodResponse_SendsHostnameIpAuthAndUserAgent()
    {
        var handler = new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("good 203.0.113.10") });
        using var httpClient = new HttpClient(handler);
        var provider = new NoIpProvider(httpClient, BuildConfig("home.example.com"));

        await provider.UpsertRecordAsync(provider.ManagedRecords[0], "203.0.113.10", CancellationToken.None);

        handler.LastRequest.Should().NotBeNull();
        var query = handler.LastRequest!.RequestUri!.Query;
        query.Should().Contain("hostname=home.example.com");
        query.Should().Contain("myip=203.0.113.10");
        handler.LastRequest.Headers.Authorization.Should().NotBeNull();
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Basic");
        handler.LastRequest.Headers.UserAgent.ToString().Should().NotBeEmpty();
    }

    [Fact]
    public async Task UpsertRecordAsync_NochgResponse_DoesNotThrow()
    {
        using var httpClient = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("nochg 203.0.113.10") }));
        var provider = new NoIpProvider(httpClient, BuildConfig("home.example.com"));

        var act = () => provider.UpsertRecordAsync(provider.ManagedRecords[0], "203.0.113.10", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("badauth")]
    [InlineData("nohost")]
    [InlineData("abuse")]
    [InlineData("911")]
    public async Task UpsertRecordAsync_ErrorResponse_Throws(string code)
    {
        using var httpClient = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(code) }));
        var provider = new NoIpProvider(httpClient, BuildConfig("home.example.com"));

        var act = () => provider.UpsertRecordAsync(provider.ManagedRecords[0], "203.0.113.10", CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage($"*{code}*");
    }
}
