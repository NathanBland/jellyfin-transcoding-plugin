using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.CommercialSkipper.Configuration;

namespace Jellyfin.Plugin.CommercialSkipper.Analysis;

public static class ComskipIniBuilder
{
    private const string DefaultProfile = """
        ; Commercial Skipper US OTA starter profile.
        detect_method=43
        validate_silence=1
        validate_uniform=1
        validate_scenechange=1
        max_brightness=60
        test_brightness=40
        max_avg_brightness=25
        max_commercialbreak=600
        min_commercialbreak=25
        max_commercial_size=125
        min_commercial_size=4
        min_show_segment_length=250
        intelligent_brightness=1
        logo_threshold=0.75
        punish_no_logo=1
        live_tv=0
        hardware_decode=0
        output_default=0
        output_edl=1
        edl_skip_field=0
        output_ffmeta=0
        output_framearray=0
        output_data=0
        output_videoredo=0
        output_videoredo3=0
        output_demux=0
        """;

    private static readonly IReadOnlyDictionary<string, string> ForcedValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["live_tv"] = "0",
        ["hardware_decode"] = "0",
        ["output_default"] = "0",
        ["output_edl"] = "1",
        ["edl_skip_field"] = "0",
        ["output_ffmeta"] = "0",
        ["output_framearray"] = "0",
        ["output_data"] = "0",
        ["output_videoredo"] = "0",
        ["output_videoredo3"] = "0",
        ["output_demux"] = "0"
    };

    public static string Build(PluginConfiguration configuration)
    {
        var source = string.IsNullOrWhiteSpace(configuration.CustomIniPath)
            ? DefaultProfile
            : File.ReadAllText(configuration.CustomIniPath);
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var output = new StringBuilder();
        foreach (var line in source.Split(['\r', '\n'], StringSplitOptions.None))
        {
            var separator = line.IndexOf('=');
            var key = separator > 0 ? line[..separator].Trim() : string.Empty;
            if (ForcedValues.TryGetValue(key, out var value))
            {
                output.Append(key).Append('=').AppendLine(value);
                found.Add(key);
            }
            else
            {
                output.AppendLine(line);
            }
        }

        foreach (var pair in ForcedValues.Where(pair => !found.Contains(pair.Key)))
        {
            output.Append(pair.Key).Append('=').AppendLine(pair.Value);
        }

        return output.ToString();
    }

    public static string ComputeHash(string executable, string ini, PluginConfiguration configuration)
    {
        var detector = new FileInfo(executable);
        var material = string.Join(
            '\n',
            executable,
            detector.Exists ? detector.Length : -1,
            detector.Exists ? detector.LastWriteTimeUtc.Ticks : -1,
            ini,
            configuration.ComskipThreads,
            configuration.PlayNice);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }
}
