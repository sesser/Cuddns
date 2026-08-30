using System.Net;
using System.Net.Http;
using Cuddns.PublicIp;
using FluentAssertions;

namespace Cuddns.Tests.PublicIp;

public class IdentMePublicIpSourceTests
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
        var sut = new IdentMePublicIpSource(httpClient);

        var ip = await sut.TryGetIpAsync(IpFamily.IPv4, CancellationToken.None);

        ip.Should().Be("203.0.113.10");
        handler.LastRequestUri!.ToString().Should().Be("https://v4.ident.me/");
    }

    [Fact]
    public async Task TryGetIpAsync_IPv6_HitsV6EndpointAndReturnsIp()
    {
        var handler = new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("2001:db8::1") });
        using var httpClient = new HttpClient(handler);
        var sut = new IdentMePublicIpSource(httpClient);

        var ip = await sut.TryGetIpAsync(IpFamily.IPv6, CancellationToken.None);

        ip.Should().Be("2001:db8::1");
        handler.LastRequestUri!.ToString().Should().Be("https://v6.ident.me/");
    }
}
