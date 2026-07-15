using Jellyfin.Plugin.TranscodingPolicy.Patching;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.TranscodingPolicy.Api;

/// <summary>
/// Exposes administrator-only diagnostics for the configuration page.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("TranscodingPolicy")]
public sealed class TranscodingPolicyController : ControllerBase
{
    /// <summary>
    /// Gets the current patch state.
    /// </summary>
    [HttpGet("Status")]
    [ProducesResponseType(typeof(PatchStatus), StatusCodes.Status200OK)]
    public ActionResult<PatchStatus> GetStatus() => Ok(EncodingPolicyPatch.Status);
}

