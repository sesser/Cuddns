using Cuddns.PublicIp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cuddns.Tests.PublicIp;

public class PublicIpResolverTests
{
    private sealed class FakeSource(
        string name, Func<IpFamily, string?>? respond = null, Exception? throwFor = null) : IPublicIpSource
    {
        public string Name => name;

        public List<IpFamily> Requests { get; } = [];

        public Task<string?> TryGetIpAsync(IpFamily family, CancellationToken cancellationToken)
        {
            Requests.Add(family);
            if (throwFor is not null)
            {
                throw throwFor;
            }

            return Task.FromResult(respond?.Invoke(family));
        }
    }

    private static PublicIpResolver CreateSut(params IPublicIpSource[] sources) =>
        new(sources, enableIpv6: true, NullLogger<PublicIpResolver>.Instance);

    [Fact]
    public async Task GetCurrentIpsAsync_FirstSourceAnswersBothFamilies_ReturnsThem()
    {
        var source = new FakeSource("only", family => family == IpFamily.IPv4 ? "203.0.113.10" : "2001:db8::1");

        var result = await CreateSut(source).GetCurrentIpsAsync(CancellationToken.None);

        result.IPv4.Should().Be("203.0.113.10");
        result.IPv6.Should().Be("2001:db8::1");
    }

    [Fact]
    public async Task GetCurrentIpsAsync_FirstSourceReturnsNullForFamily_FallsThroughToNextSource()
    {
        // e.g. ifconfig.net (IPv4-only) followed by a family-pinned source for IPv6.
        var ipv4Only = new FakeSource("ipv4only", family => family == IpFamily.IPv4 ? "203.0.113.10" : null);
        var dualStack = new FakeSource("dual", family => family == IpFamily.IPv4 ? "203.0.113.99" : "2001:db8::1");

        var result = await CreateSut(ipv4Only, dualStack).GetCurrentIpsAsync(CancellationToken.None);

        result.IPv4.Should().Be("203.0.113.10");
        result.IPv6.Should().Be("2001:db8::1");
    }

    [Fact]
    public async Task GetCurrentIpsAsync_SourceThrows_FallsThroughToNextSource()
    {
        var failing = new FakeSource("failing", throwFor: new InvalidOperationException("boom"));
        var backup = new FakeSource("backup", _ => "203.0.113.10");

        var result = await CreateSut(failing, backup).GetCurrentIpsAsync(CancellationToken.None);

        result.IPv4.Should().Be("203.0.113.10");
    }

    [Fact]
    public async Task GetCurrentIpsAsync_NoSourceAnswersEitherFamily_Throws()
    {
        var noAnswer = new FakeSource("noanswer", _ => null);

        var act = () => CreateSut(noAnswer).GetCurrentIpsAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetCurrentIpsAsync_OnlyIPv6Available_ReturnsNullIPv4WithoutThrowing()
    {
        var v6Only = new FakeSource("v6only", family => family == IpFamily.IPv6 ? "2001:db8::1" : null);

        var result = await CreateSut(v6Only).GetCurrentIpsAsync(CancellationToken.None);

        result.IPv4.Should().BeNull();
        result.IPv6.Should().Be("2001:db8::1");
    }

    [Fact]
    public async Task GetCurrentIpsAsync_Ipv6Disabled_NeverQueriesSourcesForIpv6()
    {
        var dualStack = new FakeSource("dual", family => family == IpFamily.IPv4 ? "203.0.113.10" : "2001:db8::1");
        var sut = new PublicIpResolver([dualStack], enableIpv6: false, NullLogger<PublicIpResolver>.Instance);

        var result = await sut.GetCurrentIpsAsync(CancellationToken.None);

        result.IPv4.Should().Be("203.0.113.10");
        result.IPv6.Should().BeNull();
        dualStack.Requests.Should().Equal(IpFamily.IPv4);
    }
}
