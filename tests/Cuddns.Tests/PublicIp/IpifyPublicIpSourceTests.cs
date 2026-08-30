using System.Net;
using System.Net.Http;
using Cuddns.PublicIp;
using FluentAssertions;

namespace Cuddns.Tests.PublicIp;

public class IpifyPublicIpSourceTests
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

    [Fact]
    public async Task TryGetIpAsync_IPv4_HitsV4EndpointAndReturnsIp()
    {
        var handler = new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("203.0.113.10") });
        using var httpClient = new HttpClient(handler);
        var sut = new IpifyPublicIpSource(httpClient);

        var ip = await sut.TryGetIpAsync(IpFamily.IPv4, CancellationToken.None);

        ip.Should().Be("203.0.113.10");
        handler.LastRequestUri!.ToString().Should().Be("https://api.ipify.org/");
    }

    [Fact]
    public async Task TryGetIpAsync_IPv6_HitsV6EndpointAndReturnsIp()
    {
        var handler = new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("2001:db8::1") });
        using var httpClient = new HttpClient(handler);
        var sut = new IpifyPublicIpSource(httpClient);

        var ip = await sut.TryGetIpAsync(IpFamily.IPv6, CancellationToken.None);

        ip.Should().Be("2001:db8::1");
        handler.LastRequestUri!.ToString().Should().Be("https://api6.ipify.org/");
    }

    [Fact]
    public async Task TryGetIpAsync_FamilyMismatch_Throws()
    {
        // e.g. the v6-only endpoint somehow answering with a v4 address (or vice versa) should
        // never be trusted, since it'd silently write the wrong record type.
        using var httpClient = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("203.0.113.10") }));
        var sut = new IpifyPublicIpSource(httpClient);

        var act = () => sut.TryGetIpAsync(IpFamily.IPv6, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task TryGetIpAsync_UnparseableResponse_Throws()
    {
        using var httpClient = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not an ip") }));
        var sut = new IpifyPublicIpSource(httpClient);

        var act = () => sut.TryGetIpAsync(IpFamily.IPv4, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
