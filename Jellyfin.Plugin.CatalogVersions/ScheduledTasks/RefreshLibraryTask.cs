using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CatalogVersions.ScheduledTasks
{
    public class MergeMoviesTask : IScheduledTask
    {
        private readonly ILogger _logger;
        private readonly MergeVersionsManager _mergeVersionsManager;

        public MergeMoviesTask(
            MergeVersionsManager mergeVersionsManager,
            ILogger<MergeVersionsManager> logger
        )
        {
            _logger = logger;
            _mergeVersionsManager = mergeVersionsManager;
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.LogInformation("Starting plugin, Merging Movies");
            await _mergeVersionsManager.MergeMoviesAsync(progress, cancellationToken: cancellationToken);
            _logger.LogInformation("All movies merged");
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            // Enable a schedule only after validating a pilot merge.
            return Array.Empty<TaskTriggerInfo>();
        }

        public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            return Execute(cancellationToken, progress);
        }

        public string Name => "Catalog Versions: Merge All Movies";
        public string Key => "CatalogVersions.MergeMovies";
        public string Description => "Scans all libraries to merge repeated movies";
        public string Category => "Catalog Versions";
    }

    public class MergeEpisodesTask : IScheduledTask
    {
        private readonly ILogger _logger;
        private readonly MergeVersionsManager _mergeVersionsManager;

        public MergeEpisodesTask(
            MergeVersionsManager mergeVersionsManager,
            ILogger<MergeVersionsManager> logger
        )
        {
            _logger = logger;
            _mergeVersionsManager = mergeVersionsManager;
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.LogInformation("Starting plugin, Merging Episodes");
            await _mergeVersionsManager.MergeEpisodesAsync(progress, cancellationToken: cancellationToken);
            _logger.LogInformation("Merging Episodes task finished");
            return;
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            // Enable a schedule only after validating a pilot merge.
            return Array.Empty<TaskTriggerInfo>();
        }

        public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            return Execute(cancellationToken, progress);
        }

        public string Name => "Catalog Versions: Merge All Episodes";
        public string Key => "CatalogVersions.MergeEpisodes";
        public string Description => "Merges all repeated episodes";
        public string Category => "Catalog Versions";
    }
}
