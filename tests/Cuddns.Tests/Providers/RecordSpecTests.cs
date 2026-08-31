using Cuddns.Options;
using Cuddns.Providers;
using FluentAssertions;

namespace Cuddns.Tests.Providers;

public class RecordSpecTests
{
    [Fact]
    public void Parse_NoSuffix_DefaultsToA()
    {
        var (name, type) = RecordSpec.Parse("vpn.example.com");

        name.Should().Be("vpn.example.com");
        type.Should().Be(RecordType.A);
    }

    [Theory]
    [InlineData("vpn.example.com:a", RecordType.A)]
    [InlineData("vpn.example.com:A", RecordType.A)]
    [InlineData("vpn.example.com:aaaa", RecordType.AAAA)]
    [InlineData("vpn.example.com:AAAA", RecordType.AAAA)]
    public void Parse_WithSuffix_SplitsNameAndType(string raw, RecordType expectedType)
    {
        var (name, type) = RecordSpec.Parse(raw);

        name.Should().Be("vpn.example.com");
        type.Should().Be(expectedType);
    }

    [Fact]
    public void Parse_UnknownSuffix_Throws()
    {
        var act = () => RecordSpec.Parse("vpn.example.com:cname");

        act.Should().Throw<ConfigValidationException>()
            .WithMessage("*vpn.example.com:cname*");
    }
}
