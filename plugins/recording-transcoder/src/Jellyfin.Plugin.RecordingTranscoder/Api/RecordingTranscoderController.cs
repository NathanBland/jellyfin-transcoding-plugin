using Jellyfin.Plugin.RecordingPipeline;
using Jellyfin.Plugin.RecordingTranscoder.Models;
using Jellyfin.Plugin.RecordingTranscoder.Services;
using Jellyfin.Plugin.RecordingTranscoder.Transcoding;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.RecordingTranscoder.Api;

[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("RecordingTranscoder")]
public sealed class RecordingTranscoderController(
    LibraryScopeResolver scopeResolver,
    RecordingTranscoderRunner runner,
    TranscodeJobService jobService) : ControllerBase
{
    [HttpGet("Libraries")]
    public ActionResult<IReadOnlyList<LibrarySelectionInfo>> GetLibraries()
    {
        var configuration = Plugin.Instance!.Configuration;
        return Ok(scopeResolver.GetLibraries(configuration.FollowRecordingLibraries, configuration.SelectedLibraryIds));
    }

    [HttpGet("Capabilities")]
    public async Task<ActionResult<EncoderCapabilities>> GetCapabilities(CancellationToken cancellationToken)
        => Ok(await runner.GetCapabilitiesAsync(Plugin.Instance!.Configuration.Encoder, cancellationToken).ConfigureAwait(false));

    [HttpGet("Status")]
    public ActionResult<TranscodeQueueStatus> GetStatus() => Ok(jobService.GetStatus());

    [HttpPost("Encoder/Test")]
    public async Task<ActionResult<EncoderCapabilities>> TestEncoder(CancellationToken cancellationToken)
        => Ok(await runner.TestEncoderAsync(Plugin.Instance!.Configuration.Encoder, cancellationToken).ConfigureAwait(false));

    [HttpPost("Scans")]
    public async Task<ActionResult<object>> StartScan([FromBody] ScanRequest? request, CancellationToken cancellationToken)
        => Accepted(new { Queued = await jobService.EnqueueAllAsync(request?.Force ?? false, cancellationToken).ConfigureAwait(false) });

    [HttpDelete("Jobs/{itemId:guid}")]
    public async Task<IActionResult> Cancel(Guid itemId, CancellationToken cancellationToken)
        => await jobService.CancelAsync(itemId, cancellationToken).ConfigureAwait(false) ? NoContent() : NotFound();
}
