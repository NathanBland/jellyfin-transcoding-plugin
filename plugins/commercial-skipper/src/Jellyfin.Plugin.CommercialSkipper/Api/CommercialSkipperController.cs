using Jellyfin.Plugin.CommercialSkipper.Analysis;
using Jellyfin.Plugin.CommercialSkipper.Models;
using Jellyfin.Plugin.CommercialSkipper.Services;
using Jellyfin.Plugin.RecordingPipeline;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.CommercialSkipper.Api;

[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("CommercialSkipper")]
public sealed class CommercialSkipperController(
    LibraryScopeResolver scopeResolver,
    CommercialJobService jobService,
    ComskipRunner comskipRunner) : ControllerBase
{
    [HttpGet("Libraries")]
    public ActionResult<IReadOnlyList<LibrarySelectionInfo>> GetLibraries()
    {
        var configuration = Plugin.Instance!.Configuration;
        return Ok(scopeResolver.GetLibraries(configuration.FollowRecordingLibraries, configuration.SelectedLibraryIds));
    }

    [HttpGet("Status")]
    public ActionResult<CommercialQueueStatus> GetStatus() => Ok(jobService.GetStatus());

    [HttpPost("Detector/Test")]
    public async Task<ActionResult<ProcessResult>> TestDetector(CancellationToken cancellationToken)
        => Ok(await comskipRunner.TestAsync(Plugin.Instance!.Configuration, cancellationToken).ConfigureAwait(false));

    [HttpPost("Scans")]
    public ActionResult<object> StartScan([FromBody] ScanRequest? request)
        => Accepted(new { Queued = jobService.EnqueueAll(request?.Force ?? false) });

    [HttpDelete("Jobs/{itemId:guid}")]
    public IActionResult Cancel(Guid itemId)
        => jobService.Cancel(itemId) ? NoContent() : NotFound();

    [HttpDelete("Segments")]
    public async Task<IActionResult> ClearSegments(CancellationToken cancellationToken)
    {
        await jobService.ClearAllAsync(cancellationToken).ConfigureAwait(false);
        return NoContent();
    }
}
