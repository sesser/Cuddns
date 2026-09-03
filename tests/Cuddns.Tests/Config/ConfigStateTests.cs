using Cuddns.Config;
using Cuddns.Options;
using Cuddns.Providers;
using Cuddns.PublicIp;
using FluentAssertions;
using Moq;

namespace Cuddns.Tests.Config;

public class ConfigStateTests
{
    private static ConfigSnapshot BuildSnapshot(params IDnsProvider[] providers) =>
        new(new CuddnsOptions(), providers, Mock.Of<IPublicIpResolver>());

    [Fact]
    public void Current_ReturnsInitialSnapshot()
    {
        var initial = BuildSnapshot();
        var state = new ConfigState(initial);

        state.Current.Should().BeSameAs(initial);
    }

    [Fact]
    public void Replace_UpdatesCurrent()
    {
        var state = new ConfigState(BuildSnapshot());
        var next = BuildSnapshot();

        state.Replace(next);

        state.Current.Should().BeSameAs(next);
    }

    [Fact]
    public void Replace_DisposesDisposableProvidersFromPreviousSnapshot()
    {
        var disposable = new Mock<IDnsProvider>();
        var asDisposable = disposable.As<IDisposable>();
        var state = new ConfigState(BuildSnapshot(disposable.Object));

        state.Replace(BuildSnapshot());

        asDisposable.Verify(d => d.Dispose(), Times.Once);
    }

    [Fact]
    public void Replace_NonDisposableProvider_DoesNotThrow()
    {
        var provider = new Mock<IDnsProvider>();
        var state = new ConfigState(BuildSnapshot(provider.Object));

        var act = () => state.Replace(BuildSnapshot());

        act.Should().NotThrow();
    }
}
