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

        public string Name => "Catalog Versions: 合并 AV";
        public string Key => "CatalogVersions.MergeMovies";
        public string Description => "合并 AV 目录中的匹配影片";
        public string Category => "Catalog Versions";
    }

    public class SplitMoviesTask : IScheduledTask
    {
        private readonly ILogger _logger;
        private readonly MergeVersionsManager _mergeVersionsManager;

        public SplitMoviesTask(
            MergeVersionsManager mergeVersionsManager,
            ILogger<MergeVersionsManager> logger
        )
        {
            _logger = logger;
            _mergeVersionsManager = mergeVersionsManager;
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.LogInformation("Starting plugin, Splitting AV");
            await _mergeVersionsManager.SplitMoviesAsync(progress, cancellationToken: cancellationToken);
            _logger.LogInformation("Splitting AV task finished");
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

        public string Name => "Catalog Versions: 拆分 AV";
        public string Key => "CatalogVersions.SplitMovies";
        public string Description => "拆分 AV 目录中的已关联版本";
        public string Category => "Catalog Versions";
    }
}
