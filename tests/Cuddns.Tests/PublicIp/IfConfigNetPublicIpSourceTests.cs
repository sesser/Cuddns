using System.Net;
using System.Net.Http;
using Cuddns.PublicIp;
using FluentAssertions;

namespace Cuddns.Tests.PublicIp;

public class IfConfigNetPublicIpSourceTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }

    [Fact]
    public async Task TryGetIpAsync_IPv4_ValidIpResponse_ReturnsIp()
    {
        using var httpClient = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("203.0.113.10\n") }));
        var sut = new IfConfigNetPublicIpSource(httpClient);

        var ip = await sut.TryGetIpAsync(IpFamily.IPv4, CancellationToken.None);

        ip.Should().Be("203.0.113.10");
    }

    [Fact]
    public async Task TryGetIpAsync_IPv6_ReturnsNullWithoutMakingARequest()
    {
        var requested = false;
        using var httpClient = new HttpClient(new StubHandler(_ =>
        {
            requested = true;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("2001:db8::1") };
        }));
        var sut = new IfConfigNetPublicIpSource(httpClient);

        var ip = await sut.TryGetIpAsync(IpFamily.IPv6, CancellationToken.None);

        ip.Should().BeNull();
        requested.Should().BeFalse();
    }

    [Fact]
    public async Task TryGetIpAsync_HtmlResponse_ThrowsInsteadOfReturningIt()
    {
        // Regression test: ifconfig.net serves a full HTML page instead of the plain-text IP
        // for clients it doesn't recognize as a CLI tool. That HTML must never be trusted as
        // an IP address (it was previously written straight into a Route53 record).
        using var httpClient = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<!DOCTYPE html><html><body>What is my IP?</body></html>"),
            }));
        var sut = new IfConfigNetPublicIpSource(httpClient);

        var act = () => sut.TryGetIpAsync(IpFamily.IPv4, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task TryGetIpAsync_TransientFailureThenSuccess_RetriesAndReturnsIp()
    {
        var callCount = 0;
        using var httpClient = new HttpClient(new StubHandler(_ =>
        {
            callCount++;
            return callCount == 1
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("198.51.100.20") };
        }));
        var sut = new IfConfigNetPublicIpSource(httpClient);

        var ip = await sut.TryGetIpAsync(IpFamily.IPv4, CancellationToken.None);

        ip.Should().Be("198.51.100.20");
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task TryGetIpAsync_AlwaysFails_ThrowsWrappingLastError()
    {
        using var httpClient = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var sut = new IfConfigNetPublicIpSource(httpClient);

        var act = () => sut.TryGetIpAsync(IpFamily.IPv4, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithInnerException<HttpRequestException>();
    }
}
