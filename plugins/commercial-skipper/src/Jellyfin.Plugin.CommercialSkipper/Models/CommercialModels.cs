using Jellyfin.Plugin.RecordingPipeline;

namespace Jellyfin.Plugin.CommercialSkipper.Models;

public sealed record CommercialRange(long StartTicks, long EndTicks);

public enum CommercialAnalysisStatus
{
    Pending,
    Succeeded,
    Failed
}

public sealed record CommercialAnalysisRecord
{
    public int SchemaVersion { get; init; } = 1;

    public required Guid ItemId { get; init; }

    public required string SourcePath { get; init; }

    public required SourceFingerprint SourceFingerprint { get; init; }

    public required string ConfigurationHash { get; init; }

    public required CommercialAnalysisStatus Status { get; init; }

    public required DateTime LastAttemptUtc { get; init; }

    public DateTime? LastSuccessUtc { get; init; }

    public string? DetectorPath { get; init; }

    public int? ExitCode { get; init; }

    public string? Error { get; init; }

    public IReadOnlyList<CommercialRange> Segments { get; init; } = [];
}

public sealed record CommercialQueueStatus(
    int Queued,
    IReadOnlyList<Guid> ActiveItemIds,
    int SuccessfulResults,
    int FailedResults,
    string? LastError);

public sealed record ScanRequest(bool Force = false);
