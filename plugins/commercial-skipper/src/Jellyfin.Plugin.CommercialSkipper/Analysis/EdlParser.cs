using System.Globalization;
using Jellyfin.Plugin.CommercialSkipper.Models;

namespace Jellyfin.Plugin.CommercialSkipper.Analysis;

public static class EdlParser
{
    private const double MergeToleranceSeconds = 0.250;

    public static IReadOnlyList<CommercialRange> Parse(string content, long? runtimeTicks)
    {
        var runtimeSeconds = runtimeTicks is > 0 ? runtimeTicks.Value / (double)TimeSpan.TicksPerSecond : (double?)null;
        var ranges = new List<(double Start, double End)>();
        var lineNumber = 0;
        foreach (var rawLine in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2
                || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var start)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var end)
                || !double.IsFinite(start)
                || !double.IsFinite(end))
            {
                throw new FormatException($"Invalid EDL line {lineNumber}: {line}");
            }

            start = Math.Max(0, start);
            if (runtimeSeconds.HasValue)
            {
                end = Math.Min(end, runtimeSeconds.Value);
            }

            if (end <= start || (runtimeSeconds.HasValue && start >= runtimeSeconds.Value))
            {
                throw new FormatException($"Invalid EDL range on line {lineNumber}: {line}");
            }

            ranges.Add((start, end));
        }

        var merged = new List<(double Start, double End)>();
        foreach (var range in ranges.OrderBy(range => range.Start).ThenBy(range => range.End))
        {
            if (merged.Count > 0 && range.Start <= merged[^1].End + MergeToleranceSeconds)
            {
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, range.End));
            }
            else
            {
                merged.Add(range);
            }
        }

        return merged.Select(range => new CommercialRange(
                (long)Math.Round(range.Start * TimeSpan.TicksPerSecond, MidpointRounding.AwayFromZero),
                (long)Math.Round(range.End * TimeSpan.TicksPerSecond, MidpointRounding.AwayFromZero)))
            .ToArray();
    }
}
