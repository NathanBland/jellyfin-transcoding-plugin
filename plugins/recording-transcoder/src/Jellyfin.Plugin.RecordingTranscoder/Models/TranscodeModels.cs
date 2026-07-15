using Jellyfin.Plugin.RecordingPipeline;

namespace Jellyfin.Plugin.RecordingTranscoder.Models;

public enum TranscodeStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Skipped
}

public sealed record TranscodeRecord
{
    public int SchemaVersion { get; init; } = 1;

    public required Guid ItemId { get; init; }

    public required string SourcePath { get; init; }

    public required SourceFingerprint OriginalFingerprint { get; init; }

    public SourceFingerprint? OutputFingerprint { get; init; }

    public required TranscodeStatus Status { get; init; }

    public required DateTime UpdatedUtc { get; init; }

    public string? Encoder { get; init; }

    public long? OriginalBytes { get; init; }

    public long? OutputBytes { get; init; }

    public string? Error { get; init; }
}

public enum TransactionStage
{
    Encoding,
    Validated,
    BackupCreated,
    Published,
    Refreshed
}

internal enum TransactionRecoveryAction
{
    FinishPublished,
    RestoreBackup,
    CleanupIncomplete,
    PreserveOnlyCopy
}

public sealed record TransactionJournal
{
    public int SchemaVersion { get; init; } = 1;

    public required Guid JobId { get; init; }

    public required Guid ItemId { get; init; }

    public required string SourcePath { get; init; }

    public required string TemporaryPath { get; init; }

    public required string BackupPath { get; init; }

    public required SourceFingerprint OriginalFingerprint { get; init; }

    public required TransactionStage Stage { get; init; }

    public required DateTime UpdatedUtc { get; init; }
}

public sealed record EncoderCandidateSpec(
    string Encoder,
    string Codec,
    bool IsHardware,
    string Backend);

public sealed record EncoderSelection(
    string Encoder,
    string Codec,
    bool IsHardware,
    string Backend,
    string PixelFormat,
    string? Profile,
    IReadOnlyList<string> InitializationArguments,
    bool RequiresHardwareUpload,
    bool SupportsA53ClosedCaptions,
    bool SupportsPowerEfficient,
    bool SupportsSpatialAq,
    bool SupportsPrioritySpeed);

public sealed record EncoderCapabilities(
    string FfmpegPath,
    string FfprobePath,
    string JellyfinHardwareAcceleration,
    bool JellyfinHardwareEncodingEnabled,
    bool JellyfinAllowAv1Encoding,
    bool JellyfinAllowHevcEncoding,
    IReadOnlyList<string> AvailableEncoders,
    EncoderSelection? Selected,
    bool EncoderSessionVerified,
    bool UsedSoftwareFallback,
    string? FallbackReason,
    string? EncoderTestError,
    string RawEncoders)
{
    public string? SelectedEncoder => Selected?.Encoder;

    public string? SelectedProfile => Selected?.Profile;
}

public sealed record ProbeStream(
    int Index,
    string CodecType,
    string CodecName,
    string Profile,
    int Width,
    int Height,
    double FramesPerSecond,
    string FieldOrder,
    long BitRate,
    bool HasClosedCaptions,
    string ColorSpace = "",
    string ColorTransfer = "",
    string ColorPrimaries = "",
    string ColorRange = "");

public sealed record MediaProbe(
    double DurationSeconds,
    long BitRate,
    IReadOnlyList<ProbeStream> Streams)
{
    public ProbeStream? Video => Streams.FirstOrDefault(stream => stream.CodecType == "video");

    public int AudioCount => Streams.Count(stream => stream.CodecType == "audio");

    public int SubtitleCount => Streams.Count(stream => stream.CodecType == "subtitle");

    public int AttachmentCount => Streams.Count(stream => stream.CodecType == "attachment");
}

public sealed record TranscodeValidation(bool IsValid, string Message, MediaProbe? OutputProbe = null);

public sealed record TranscodeQueueStatus(
    int Queued,
    IReadOnlyList<Guid> ActiveItemIds,
    int Successful,
    int Failed,
    int Skipped,
    string? LastError);

public sealed record ScanRequest(bool Force = false);
