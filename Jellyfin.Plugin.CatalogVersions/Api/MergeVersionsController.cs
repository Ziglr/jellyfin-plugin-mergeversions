using System.Threading.Tasks;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CatalogVersions.Api
{
    [ApiController]
    [Authorize(Policy = Policies.RequiresElevation)]
    [Route("CatalogVersions")]
    public class MergeVersionsController : ControllerBase
    {
        private readonly MergeVersionsManager _manager;

        public MergeVersionsController(MergeVersionsManager manager, ILogger<MergeVersionsManager> logger)
        {
            _manager = manager;
        }

        [HttpPost("MergeMovies")]
        public async Task<ActionResult> MergeMoviesRequestAsync()
        {
            await _manager.MergeMoviesAsync(null, User, HttpContext.RequestAborted).ConfigureAwait(false);
            return NoContent();
        }

        [HttpPost("SplitMovies")]
        public async Task<ActionResult> SplitMoviesRequestAsync()
        {
            await _manager.SplitMoviesAsync(null, User, HttpContext.RequestAborted).ConfigureAwait(false);
            return NoContent();
        }
    }
}
