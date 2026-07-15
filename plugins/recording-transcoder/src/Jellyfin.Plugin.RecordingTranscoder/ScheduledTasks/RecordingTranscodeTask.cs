using Jellyfin.Plugin.RecordingTranscoder.Services;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.RecordingTranscoder.ScheduledTasks;

public sealed class RecordingTranscodeTask(TranscodeJobService jobService) : IScheduledTask
{
    public string Name => "Transcode completed recordings";

    public string Key => "RecordingTranscoderScan";

    public string Description => "Queues selected completed recordings for safe hardware recompression.";

    public string Category => "Recording Transcoder";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _ = await jobService.EnqueueReconciliationAsync(cancellationToken).ConfigureAwait(false);
        progress.Report(100);
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromHours(6).Ticks
        };
    }
}
