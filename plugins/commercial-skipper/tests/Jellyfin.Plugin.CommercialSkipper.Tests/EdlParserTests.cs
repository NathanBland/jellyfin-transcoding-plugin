using Jellyfin.Plugin.CommercialSkipper.Analysis;

namespace Jellyfin.Plugin.CommercialSkipper.Tests;

public sealed class EdlParserTests
{
    [Fact]
    public void Parse_MultipleRanges_SortsAndMergesOverlaps()
    {
        var result = EdlParser.Parse("60.10 90.00 0\n10.00 30.00 0\n29.90 45.00 0\n", null);

        Assert.Collection(
            result,
            segment =>
            {
                Assert.Equal(TimeSpan.FromSeconds(10).Ticks, segment.StartTicks);
                Assert.Equal(TimeSpan.FromSeconds(45).Ticks, segment.EndTicks);
            },
            segment =>
            {
                Assert.Equal(TimeSpan.FromSeconds(60.1).Ticks, segment.StartTicks);
                Assert.Equal(TimeSpan.FromSeconds(90).Ticks, segment.EndTicks);
            });
    }

    [Fact]
    public void Parse_EmptyFile_ReturnsNoSegments()
        => Assert.Empty(EdlParser.Parse(string.Empty, null));

    [Fact]
    public void Parse_ClampsToRuntime()
    {
        var result = EdlParser.Parse("90.0 120.0 0", TimeSpan.FromSeconds(100).Ticks);
        Assert.Equal(TimeSpan.FromSeconds(100).Ticks, Assert.Single(result).EndTicks);
    }

    [Theory]
    [InlineData("abc 3 0")]
    [InlineData("3 2 0")]
    [InlineData("NaN 4 0")]
    public void Parse_InvalidLine_Throws(string line)
        => Assert.Throws<FormatException>(() => EdlParser.Parse(line, null));
}
