using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.RecordingPipeline;

public sealed record SourceFingerprint(long Length, DateTime LastWriteUtc)
{
    public static SourceFingerprint FromPath(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("Media file does not exist.", path);
        }

        return new SourceFingerprint(info.Length, info.LastWriteTimeUtc);
    }
}

public enum PipelineStage
{
    Transcode,
    CommercialScan
}

public enum PipelineLeaseState
{
    Pending,
    Running
}

public sealed record PipelineLeaseDocument
{
    public int SchemaVersion { get; init; } = 1;

    public required Guid ItemId { get; init; }

    public required string SourcePath { get; init; }

    public required SourceFingerprint SourceFingerprint { get; init; }

    public required Guid OwnerPluginId { get; init; }

    public required PipelineStage Stage { get; init; }

    public required PipelineLeaseState State { get; init; }

    public required Guid LeaseId { get; init; }

    public required DateTime UpdatedUtc { get; init; }
}

public sealed record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    TimeSpan Duration)
{
    [JsonIgnore]
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}

public sealed record LibrarySelectionInfo(
    string Id,
    string Name,
    IReadOnlyList<string> Locations,
    bool IsDvrManaged,
    bool IsExplicitlySelected,
    bool IsEffective,
    bool IsAvailable = true);
