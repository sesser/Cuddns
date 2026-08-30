using Cuddns.Logging;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cuddns.Tests.Logging;

public class CuddnsConsoleFormatterTests
{
    private static string Format(LogLevel level, string category, string message, Exception? exception = null)
    {
        var entry = new LogEntry<string>(level, category, new EventId(0), message, exception, (state, _) => state);
        using var writer = new StringWriter();
        new CuddnsConsoleFormatter().Write(entry, scopeProvider: null, writer);
        return writer.ToString();
    }

    [Theory]
    [InlineData(LogLevel.Trace, "[TRACE]")]
    [InlineData(LogLevel.Debug, "[DEBUG]")]
    [InlineData(LogLevel.Information, "[ INFO]")]
    [InlineData(LogLevel.Warning, "[ WARN]")]
    [InlineData(LogLevel.Error, "[ERROR]")]
    [InlineData(LogLevel.Critical, "[ CRIT]")]
    public void Write_UsesPaddedBracketedLevelLabel(LogLevel level, string expectedLabel)
    {
        var output = Format(level, "Cuddns.Some.Category", "hello");

        output.Should().Contain(expectedLabel);
    }

    [Fact]
    public void Write_UsesOnlyTheLastSegmentOfTheCategory()
    {
        var output = Format(LogLevel.Information, "Cuddns.Providers.DuckDns.DuckDnsProviderFactory", "hi");

        output.Should().Contain("DuckDnsProviderFactory:").And.NotContain("Cuddns.Providers");
    }

    [Fact]
    public void Write_CategoryWithNoDot_UsesItUnchanged()
    {
        var output = Format(LogLevel.Information, "Program", "hi");

        output.Should().Contain("Program:");
    }

    [Fact]
    public void Write_WithException_KeepsEverythingOnOneLine()
    {
        var output = Format(LogLevel.Error, "Category", "failed", new InvalidOperationException("boom"));

        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines.Should().ContainSingle();
        output.Should().Contain("boom");
    }
}
