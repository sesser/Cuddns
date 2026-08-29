using System.Net;
using System.Net.Http;
using Cuddns.PublicIp;
using FluentAssertions;

namespace Cuddns.Tests.PublicIp;

public class IfConfigNetPublicIpProviderTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }

    [Fact]
    public async Task GetCurrentIpAsync_ValidIpResponse_ReturnsIp()
    {
        using var httpClient = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("203.0.113.10\n") }));
        var sut = new IfConfigNetPublicIpProvider(httpClient);

        var ip = await sut.GetCurrentIpAsync(CancellationToken.None);

        ip.Should().Be("203.0.113.10");
    }

    [Fact]
    public async Task GetCurrentIpAsync_HtmlResponse_ThrowsInsteadOfReturningIt()
    {
        // Regression test: ifconfig.net serves a full HTML page instead of the plain-text IP
        // for clients it doesn't recognize as a CLI tool. That HTML must never be trusted as
        // an IP address (it was previously written straight into a Route53 record).
        using var httpClient = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<!DOCTYPE html><html><body>What is my IP?</body></html>"),
            }));
        var sut = new IfConfigNetPublicIpProvider(httpClient);

        var act = () => sut.GetCurrentIpAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetCurrentIpAsync_TransientFailureThenSuccess_RetriesAndReturnsIp()
    {
        var callCount = 0;
        using var httpClient = new HttpClient(new StubHandler(_ =>
        {
            callCount++;
            return callCount == 1
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("198.51.100.20") };
        }));
        var sut = new IfConfigNetPublicIpProvider(httpClient);

        var ip = await sut.GetCurrentIpAsync(CancellationToken.None);

        ip.Should().Be("198.51.100.20");
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task GetCurrentIpAsync_AlwaysFails_ThrowsWrappingLastError()
    {
        using var httpClient = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var sut = new IfConfigNetPublicIpProvider(httpClient);

        var act = () => sut.GetCurrentIpAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithInnerException<HttpRequestException>();
    }
}
