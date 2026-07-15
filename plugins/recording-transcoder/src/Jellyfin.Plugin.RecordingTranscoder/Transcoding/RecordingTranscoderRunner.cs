using Jellyfin.Plugin.RecordingPipeline;
using Jellyfin.Plugin.RecordingTranscoder.Configuration;
using Jellyfin.Plugin.RecordingTranscoder.Models;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.RecordingTranscoder.Transcoding;

public sealed class RecordingTranscoderRunner(
    IMediaEncoder mediaEncoder,
    IServerConfigurationManager serverConfigurationManager,
    ProcessRunner processRunner,
    MediaProbeService probeService)
{
    public async Task<EncoderCapabilities> GetCapabilitiesAsync(
        EncoderMode mode,
        CancellationToken cancellationToken)
    {
        var encodingOptions = serverConfigurationManager.GetEncodingOptions();
        var encoders = await processRunner.RunAsync(
            mediaEncoder.EncoderPath,
            ["-hide_banner", "-encoders"],
            TimeSpan.FromSeconds(30),
            null,
            cancellationToken).ConfigureAwait(false);
        if (!encoders.Succeeded)
        {
            throw new InvalidOperationException($"Unable to enumerate FFmpeg encoders: {encoders.StandardError}");
        }

        var availableEncoders = ParseVideoEncoders(encoders.StandardOutput);
        var specs = EncoderPlanner.GetCandidateSpecs(
            mode,
            encodingOptions.HardwareAccelerationType,
            encodingOptions.EnableHardwareEncoding,
            encodingOptions.AllowAv1Encoding,
            encodingOptions.AllowHevcEncoding);
        var failures = new List<string>();
        var attemptedHardware = false;
        EncoderSelection? selected = null;
        foreach (var spec in specs)
        {
            if (!availableEncoders.Contains(spec.Encoder))
            {
                failures.Add($"{spec.Encoder}: not provided by Jellyfin FFmpeg");
                continue;
            }

            attemptedHardware |= spec.IsHardware;
            foreach (var candidate in await CreateSelectionsAsync(spec, encodingOptions, cancellationToken).ConfigureAwait(false))
            {
                var test = await TestSelectionAsync(candidate, cancellationToken).ConfigureAwait(false);
                if (test.Succeeded)
                {
                    selected = candidate;
                    break;
                }

                failures.Add($"{candidate.Encoder} {candidate.Profile ?? candidate.PixelFormat}: {FormatFailure(test)}");
            }

            if (selected is not null)
            {
                break;
            }
        }

        var jellyfinHardwareEnabled = encodingOptions.EnableHardwareEncoding
            && encodingOptions.HardwareAccelerationType != HardwareAccelerationType.none;
        var usedSoftwareFallback = mode == EncoderMode.FollowJellyfin && selected is { IsHardware: false };
        var fallbackReason = usedSoftwareFallback
            ? jellyfinHardwareEnabled
                ? attemptedHardware
                    ? "Jellyfin hardware encoding was enabled, but no permitted hardware encoder completed a test session."
                    : "Jellyfin hardware encoding was enabled, but its permitted encoder was not provided by Jellyfin FFmpeg."
                : "Jellyfin hardware encoding is disabled; using a Jellyfin-FFmpeg software encoder."
            : null;
        return new EncoderCapabilities(
            mediaEncoder.EncoderPath,
            mediaEncoder.ProbePath,
            encodingOptions.HardwareAccelerationType.ToString(),
            jellyfinHardwareEnabled,
            encodingOptions.AllowAv1Encoding,
            encodingOptions.AllowHevcEncoding,
            availableEncoders.Order(StringComparer.Ordinal).ToArray(),
            selected,
            selected is not null,
            usedSoftwareFallback,
            fallbackReason,
            selected is null
                ? failures.Count > 0
                    ? string.Join("; ", failures)
                    : "No encoder candidates are permitted by the selected policy."
                : null,
            encoders.StandardOutput);
    }

    public async Task<ProcessResult> EncodeAsync(
        string sourcePath,
        string outputPath,
        MediaProbe inputProbe,
        EncoderCapabilities capabilities,
        PluginConfiguration configuration,
        CancellationToken cancellationToken)
    {
        return await processRunner.RunAsync(
            mediaEncoder.EncoderPath,
            EncoderPlanner.BuildArguments(sourcePath, outputPath, inputProbe, capabilities, configuration),
            TimeSpan.FromMinutes(Math.Clamp(configuration.TimeoutMinutes, 1, 2880)),
            Path.GetDirectoryName(sourcePath),
            cancellationToken).ConfigureAwait(false);
    }

    public Task<EncoderCapabilities> TestEncoderAsync(
        EncoderMode mode,
        CancellationToken cancellationToken)
        => GetCapabilitiesAsync(mode, cancellationToken);

    public async Task<TranscodeValidation> ValidateAsync(
        string inputPath,
        string outputPath,
        MediaProbe input,
        PluginConfiguration configuration,
        EncoderCapabilities capabilities,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
        {
            return new TranscodeValidation(false, "Encoded output is missing or empty.");
        }

        MediaProbe output;
        try
        {
            output = await probeService.ProbeAsync(outputPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new TranscodeValidation(false, $"Encoded output cannot be probed: {ex.Message}");
        }

        var inputBytes = new FileInfo(inputPath).Length;
        var outputBytes = new FileInfo(outputPath).Length;
        return ValidateProbes(input, output, inputBytes, outputBytes, configuration, capabilities.Selected);
    }

    internal static TranscodeValidation ValidateProbes(
        MediaProbe input,
        MediaProbe output,
        long inputBytes,
        long outputBytes,
        PluginConfiguration configuration,
        EncoderSelection? selection)
    {
        var inputVideo = input.Video;
        var outputVideo = output.Video;
        if (inputVideo is null || outputVideo is null || selection is null)
        {
            return new TranscodeValidation(false, "Input, output, or encoder selection is missing.", output);
        }

        if (!string.Equals(outputVideo.CodecName, selection.Codec, StringComparison.OrdinalIgnoreCase))
        {
            return new TranscodeValidation(false, $"Output codec is {outputVideo.CodecName}, not {selection.Codec}.", output);
        }

        if (outputVideo.CodecName == "hevc")
        {
            var expectsMain10 = string.Equals(selection.Profile, "main10", StringComparison.OrdinalIgnoreCase);
            var isMain10 = outputVideo.Profile.Contains("10", StringComparison.OrdinalIgnoreCase);
            if (expectsMain10 != isMain10)
            {
                return new TranscodeValidation(false, expectsMain10 ? "Output is not HEVC Main 10." : "Output is not HEVC Main.", output);
            }
        }

        if (inputVideo.Width != outputVideo.Width || inputVideo.Height != outputVideo.Height)
        {
            return new TranscodeValidation(false, "Output dimensions differ from the source.", output);
        }

        var expectedFrameRate = inputVideo.FramesPerSecond * (EncoderPlanner.IsInterlaced(inputVideo) ? 2 : 1);
        var frameRateTolerance = Math.Max(0.05, expectedFrameRate * 0.005);
        if (expectedFrameRate <= 0
            || outputVideo.FramesPerSecond <= 0
            || Math.Abs(expectedFrameRate - outputVideo.FramesPerSecond) > frameRateTolerance)
        {
            return new TranscodeValidation(false, "Output frame rate differs from the expected deinterlaced frame rate.", output);
        }

        var durationTolerance = Math.Max(1.0, input.DurationSeconds * 0.001);
        if (input.DurationSeconds <= 0 || output.DurationSeconds <= 0 || Math.Abs(input.DurationSeconds - output.DurationSeconds) > durationTolerance)
        {
            return new TranscodeValidation(false, "Output duration is outside the allowed tolerance.", output);
        }

        if (output.AudioCount != input.AudioCount
            || output.SubtitleCount != input.SubtitleCount
            || output.AttachmentCount != input.AttachmentCount)
        {
            return new TranscodeValidation(false, "Output stream counts do not match the source.", output);
        }

        if (inputVideo.HasClosedCaptions && !outputVideo.HasClosedCaptions)
        {
            return new TranscodeValidation(false, "Output did not preserve embedded closed captions.", output);
        }

        var isHd = inputVideo.Width >= 1280 || inputVideo.Height >= 720;
        if (!ColorMatches(inputVideo.ColorSpace, outputVideo.ColorSpace, isHd ? "bt709" : null)
            || !ColorMatches(inputVideo.ColorTransfer, outputVideo.ColorTransfer, isHd ? "bt709" : null)
            || !ColorMatches(inputVideo.ColorPrimaries, outputVideo.ColorPrimaries, isHd ? "bt709" : null)
            || !ColorMatches(inputVideo.ColorRange, outputVideo.ColorRange, null))
        {
            return new TranscodeValidation(false, "Output color metadata does not match the source or HD fallback.", output);
        }

        var largestAllowed = inputBytes * (1 - Math.Clamp(configuration.MinimumSavingsPercent, 0, 90) / 100d);
        if (outputBytes > largestAllowed)
        {
            return new TranscodeValidation(false, $"Output saved less than {configuration.MinimumSavingsPercent}%.", output);
        }

        return new TranscodeValidation(true, "Output passed validation.", output);
    }

    internal static HashSet<string> ParseVideoEncoders(string output)
    {
        var encoders = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2
                && parts[0].Length >= 6
                && parts[0][0] == 'V'
                && parts[1].All(character => char.IsLetterOrDigit(character) || character is '_' or '-'))
            {
                encoders.Add(parts[1]);
            }
        }

        return encoders;
    }

    private async Task<IReadOnlyList<EncoderSelection>> CreateSelectionsAsync(
        EncoderCandidateSpec spec,
        EncodingOptions encodingOptions,
        CancellationToken cancellationToken)
    {
        var helpResult = await processRunner.RunAsync(
            mediaEncoder.EncoderPath,
            ["-hide_banner", "-h", $"encoder={spec.Encoder}"],
            TimeSpan.FromSeconds(30),
            null,
            cancellationToken).ConfigureAwait(false);
        var help = helpResult.StandardOutput + helpResult.StandardError;
        var supportsTenBit = spec.Codec != "h264"
            && (spec.IsHardware
                ? help.Contains("p010le", StringComparison.OrdinalIgnoreCase)
                : help.Contains("yuv420p10le", StringComparison.OrdinalIgnoreCase));
        var bitDepths = supportsTenBit ? new[] { true, false } : [false];
        return bitDepths.Select(useTenBit => new EncoderSelection(
                spec.Encoder,
                spec.Codec,
                spec.IsHardware,
                spec.Backend,
                spec.IsHardware
                    ? useTenBit ? "p010le" : "nv12"
                    : useTenBit ? "yuv420p10le" : "yuv420p",
                spec.Codec == "hevc" ? useTenBit ? "main10" : "main" : null,
                BuildInitializationArguments(spec.Backend, encodingOptions),
                spec.Backend is "vaapi" or "qsv",
                help.Contains("a53cc", StringComparison.OrdinalIgnoreCase),
                help.Contains("power_efficient", StringComparison.Ordinal),
                help.Contains("spatial_aq", StringComparison.Ordinal),
                help.Contains("prio_speed", StringComparison.Ordinal)))
            .ToArray();
    }

    private async Task<ProcessResult> TestSelectionAsync(
        EncoderSelection selection,
        CancellationToken cancellationToken)
    {
        var directory = Plugin.Instance?.DataDirectory ?? Path.GetTempPath();
        Directory.CreateDirectory(directory);
        var outputPath = Path.Combine(directory, $".encoder-test-{Guid.NewGuid():N}.ts");
        try
        {
            var result = await processRunner.RunAsync(
                mediaEncoder.EncoderPath,
                EncoderPlanner.BuildEncoderTestArguments(selection, outputPath),
                TimeSpan.FromSeconds(45),
                directory,
                cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                return result;
            }

            var probe = await probeService.ProbeAsync(outputPath, cancellationToken).ConfigureAwait(false);
            return probe.Video is not null
                && string.Equals(probe.Video.CodecName, selection.Codec, StringComparison.OrdinalIgnoreCase)
                ? result
                : result with
                {
                    ExitCode = 1,
                    StandardError = $"Encoder test output did not probe as {selection.Codec} video."
                };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ProcessResult(1, string.Empty, ex.Message, false, TimeSpan.Zero);
        }
        finally
        {
            try
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A stale test file is harmless and must not hide the encoder result.
            }
        }
    }

    internal static IReadOnlyList<string> BuildInitializationArguments(
        string backend,
        EncodingOptions options,
        bool? isLinux = null)
    {
        if (backend == "vaapi")
        {
            var device = string.IsNullOrWhiteSpace(options.VaapiDevice) ? "/dev/dri/renderD128" : options.VaapiDevice;
            return ["-init_hw_device", $"vaapi=recording:{device}", "-filter_hw_device", "recording"];
        }

        if (backend == "qsv")
        {
            if (isLinux ?? OperatingSystem.IsLinux())
            {
                var device = !string.IsNullOrWhiteSpace(options.QsvDevice)
                    ? options.QsvDevice
                    : string.IsNullOrWhiteSpace(options.VaapiDevice) ? "/dev/dri/renderD128" : options.VaapiDevice;
                return [
                    "-init_hw_device", $"vaapi=recording_va:{device}",
                    "-init_hw_device", "qsv=recording@recording_va",
                    "-filter_hw_device", "recording"
                ];
            }

            var deviceSuffix = string.IsNullOrWhiteSpace(options.QsvDevice) ? string.Empty : $":{options.QsvDevice}";
            return ["-init_hw_device", $"qsv=recording{deviceSuffix}", "-filter_hw_device", "recording"];
        }

        return [];
    }

    private static string FormatFailure(ProcessResult result)
    {
        var message = result.TimedOut
            ? "encoder test timed out"
            : string.IsNullOrWhiteSpace(result.StandardError)
                ? $"encoder test exited with code {result.ExitCode}"
                : result.StandardError.Trim();
        const int maximumLength = 1500;
        return message.Length <= maximumLength ? message : message[^maximumLength..];
    }

    private static bool ColorMatches(string input, string output, string? missingInputFallback)
    {
        var expected = string.IsNullOrWhiteSpace(input) || input.Equals("unknown", StringComparison.OrdinalIgnoreCase)
            ? missingInputFallback
            : input;
        if (expected is null)
        {
            return true;
        }

        return string.Equals(expected, output, StringComparison.OrdinalIgnoreCase)
            || (expected == "limited" && output == "tv")
            || (expected == "full" && output == "pc")
            || (expected == "tv" && output == "limited")
            || (expected == "pc" && output == "full");
    }
}
