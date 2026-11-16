using System;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MergeVersions.Api
{
    /// <summary>
    /// The Merge Versions api controller.
    /// </summary>
    [ApiController]
    [Authorize(Policy = Policies.RequiresElevation)]
    [Route("MergeVersions")]
    [Produces(MediaTypeNames.Application.Json)]
    public class MergeVersionsController : ControllerBase
    {
        private readonly MergeVersionsManager _mergeVersionsManager;
        private readonly ILogger<MergeVersionsManager> _logger;

        /// <summary>
        /// Initializes a new instance of <see cref="MergeVersionsController"/>.
        /// </summary>
        public MergeVersionsController(
            MergeVersionsManager mergeVersionsManager,
            ILogger<MergeVersionsManager> logger
        )
        {
            _mergeVersionsManager = mergeVersionsManager;

            _logger = logger;
        }

        [HttpGet("PreviewMovies")]
        public ActionResult PreviewMovies([FromQuery] string key = null)
        {
            var groups = _mergeVersionsManager.GetMovieMergeGroups(HttpContext.RequestAborted, string.IsNullOrWhiteSpace(key) ? null : key.Trim());
            var matches = groups.Where(group => !group.Key.StartsWith("tmdb:", StringComparison.Ordinal)).ToArray();

            return Ok(new
            {
                Total = matches.Length,
                Groups = matches.Take(50).Select(group => new
                {
                    group.Key,
                    Movies = group.Movies.Select(movie => new { movie.Id, movie.Name, movie.Path })
                })
            });
        }

        /// <summary>
        /// Scans all movies and merges repeated ones.
        /// </summary>
        /// <reponse code="204">Library scan and merge started successfully. </response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("MergeMovies")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<ActionResult> MergeMoviesRequestAsync()
        {
            _logger.LogInformation("Starting a manual refresh, looking up for repeated versions");
            await _mergeVersionsManager.MergeMoviesAsync(null, User, HttpContext.RequestAborted);
            _logger.LogInformation("Completed refresh");
            return NoContent();
        }

        [HttpPost("MergeCatalogMovie")]
        public async Task<ActionResult> MergeCatalogMovieRequestAsync([FromQuery] string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return BadRequest("Enter a catalog number.");
            }

            var merged = await _mergeVersionsManager.MergeCatalogMovieAsync(key, User, HttpContext.RequestAborted);
            if (merged == 0)
            {
                return NotFound("No eligible version group for this catalog number.");
            }

            return NoContent();
        }

        /// <summary>
        /// Scans all movies and splits merged ones.
        /// </summary>
        /// <reponse code="204">Library scan and split started successfully. </response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("SplitMovies")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<ActionResult> SplitMoviesRequestAsync()
        {
            _logger.LogInformation("Splitting all movies");
            await _mergeVersionsManager.SplitMoviesAsync(null, User, HttpContext.RequestAborted);
            _logger.LogInformation("Completed");
            return NoContent();
        }

        /// <summary>
        /// Scans all episodes and merge repeated ones.
        /// </summary>
        /// <reponse code="204">Library scan and merge started successfully. </response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("MergeEpisodes")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<ActionResult> MergeEpisodesRequestAsync()
        {
            _logger.LogInformation("Starting a manual refresh, looking up for repeated versions");
            await _mergeVersionsManager.MergeEpisodesAsync(null, User, HttpContext.RequestAborted);
            _logger.LogInformation("Completed refresh");
            return NoContent();
        }

        /// <summary>
        /// Scans all episodes and splits merged ones.
        /// </summary>
        /// <reponse code="204">Library scan and split started successfully. </response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("SplitEpisodes")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<ActionResult> SplitEpisodesRequestAsync()
        {
            _logger.LogInformation("Splitting all episodes");
            await _mergeVersionsManager.SplitEpisodesAsync(null, User, HttpContext.RequestAborted);
            _logger.LogInformation("Completed");
            return NoContent();
        }
    }
}
