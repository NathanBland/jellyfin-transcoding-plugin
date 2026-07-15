using Jellyfin.Plugin.CommercialSkipper.Services;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.CommercialSkipper.ScheduledTasks;

public sealed class CommercialScanTask(CommercialJobService jobService) : IScheduledTask
{
    public string Name => "Detect commercials in recordings";

    public string Key => "CommercialSkipperScan";

    public string Description => "Queues selected completed recordings for Comskip analysis.";

    public string Category => "Commercial Skipper";

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _ = jobService.EnqueueReconciliation();
        progress.Report(100);
        return Task.CompletedTask;
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
