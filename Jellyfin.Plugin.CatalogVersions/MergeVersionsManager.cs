using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CatalogVersions
{
    public class MergeVersionsManager : IDisposable
    {
        private static readonly Regex CatalogNumber = new(
            @"^(?:(?<prefix>FC2[ _-]*PPV)[ _-]*(?<number>[0-9]{4,10})|(?<prefix>(?=[A-Z0-9]*[A-Z])[A-Z0-9]{2,20})[ _-]+(?<number>[0-9]{2,9})|(?<prefix>[0-9]*[A-Z]{2,20})(?<number>[0-9]{2,9}))(?![A-Z0-9])",
            RegexOptions.CultureInvariant);
        private static readonly SemaphoreSlim MovieMergeLock = new(1, 1);

        private readonly ILibraryManager _libraryManager;
        private readonly Timer _timer;
        private readonly ILogger<MergeVersionsManager> _logger; // TODO logging
        private readonly IFileSystem _fileSystem;
        private readonly IVideoVersions _videoVersions;

        public MergeVersionsManager(
            ILibraryManager libraryManager,
            ILogger<MergeVersionsManager> logger,
            IFileSystem fileSystem,
            IVideoVersions videoVersions
        )
        {
            _libraryManager = libraryManager;
            _logger = logger;
            _fileSystem = fileSystem;
            _videoVersions = videoVersions;
            _timer = new Timer(_ => OnTimerElapsed(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public Task MergeMoviesAsync(IProgress<double> progress, ClaimsPrincipal user = null, CancellationToken cancellationToken = default) =>
            MergeMoviesCoreAsync(progress, user, cancellationToken);

        private async Task<int> MergeMoviesCoreAsync(IProgress<double> progress, ClaimsPrincipal user, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await MovieMergeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _logger.LogInformation("Scanning for repeated movies");
                var duplicateMovies = GetMovieMergeGroups(cancellationToken);

                var current = 0;
                foreach (var (groupKey, movies) in duplicateMovies)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var first = movies[0];
                    _logger.LogInformation("Merging {Key}: {Name} ({Year})", groupKey, first.Name, first.ProductionYear);
                    await _videoVersions.MergeAsync(movies.Select(movie => movie.Id).ToArray(), user, cancellationToken).ConfigureAwait(false);
                    current++;
                    progress?.Report(current / (double)duplicateMovies.Count * 100);
                }

                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(100);
                return current;
            }
            finally
            {
                MovieMergeLock.Release();
            }
        }

        private List<(string Key, Movie[] Movies)> GetMovieMergeGroups(CancellationToken cancellationToken, bool splitting = false)
        {
            var root = Plugin.Instance.PluginConfiguration.CatalogNumberRoot?.Trim();
            if (string.IsNullOrEmpty(root) || !Path.IsPathFullyQualified(root))
            {
                throw new InvalidOperationException("Configure an absolute AV directory before running this task.");
            }

            var catalog = new List<Movie>();
            foreach (var movie in GetMoviesFromLibrary())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrEmpty(movie.Path) && _fileSystem.ContainsSubPath(root, movie.Path))
                {
                    catalog.Add(movie);
                }
            }

            return GetMetadataGroups(catalog)
                .Where(group => CanProcessCatalogGroup(group.Movies, splitting))
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToList();
        }

        private IEnumerable<(string Key, Movie[] Movies)> GetMetadataGroups(List<Movie> movies)
        {
            var parents = Enumerable.Range(0, movies.Count).ToArray();
            var identities = new Dictionary<string, int>(StringComparer.Ordinal);
            var catalogs = new string[movies.Count];
            var titles = new string[movies.Count];
            var providerIds = new string[movies.Count];
            int Find(int index)
            {
                while (parents[index] != index)
                {
                    parents[index] = parents[parents[index]];
                    index = parents[index];
                }
                return index;
            }

            void Link(string identity, int index)
            {
                if (identities.TryGetValue(identity, out var previous))
                {
                    parents[Find(index)] = Find(previous);
                }
                else
                {
                    identities.Add(identity, index);
                }
            }

            for (var i = 0; i < movies.Count; i++)
            {
                titles[i] = Regex.Replace(Normalize(movies[i].Name).TrimEnd(), @"\s+", " ");
                var match = CatalogNumber.Match(titles[i]);
                catalogs[i] = match.Success ? GetCatalogKey(match) : null;
                // Keep the full MetaTube ID, including its scraper/source prefix.
                // IDs from different sources must not be compared by suffix alone.
                providerIds[i] = movies[i].ProviderIds.TryGetValue("MetaTube", out var id)
                    && !string.IsNullOrWhiteSpace(id) ? id.Trim() : null;
                if (providerIds[i] != null) Link("metatube:" + providerIds[i], i);
                if (titles[i].Length > 0) Link("title:" + titles[i], i);
                if (catalogs[i] != null) Link("catalog:" + catalogs[i], i);
            }

            foreach (var component in Enumerable.Range(0, movies.Count).GroupBy(Find))
            {
                var indices = component.ToArray();
                var numbers = indices.Select(i => catalogs[i]).Where(key => key != null).Distinct().ToArray();
                if (numbers.Length > 1)
                {
                    _logger.LogWarning("Skipping conflicting metadata group: catalog numbers {CatalogNumbers}; item IDs {ItemIds}",
                        string.Join(", ", numbers), string.Join(", ", indices.Select(i => movies[i].Id)));
                    continue;
                }

                var provider = indices.Select(i => providerIds[i]).FirstOrDefault(id => id != null);
                var title = indices.Select(i => titles[i]).FirstOrDefault(value => value.Length > 0);
                var key = numbers.FirstOrDefault() ?? (provider != null ? "metatube:" + provider : "title:" + title);
                yield return (key, indices.Select(i => movies[i]).ToArray());
            }
        }

        private static bool CanProcessCatalogGroup(Movie[] movies, bool splitting)
        {
            if (movies.Length < 2 || (!splitting && !movies.Any(IsUnlinked))
                || movies.Any(movie => movie.IsStacked || movie.LocalAlternateVersions.Length > 0))
            {
                return false;
            }

            var primaries = movies.Where(movie => movie.LinkedAlternateVersions.Length > 0).ToArray();
            if (primaries.Length == 0)
            {
                return !splitting && movies.All(IsUnlinked);
            }

            if (primaries.Length != 1 || primaries[0].PrimaryVersionId.HasValue)
            {
                return false;
            }

            var primary = primaries[0];
            var linked = primary.LinkedAlternateVersions.Select(item => item.ItemId).ToHashSet();
            var alternates = movies.Where(movie => movie.PrimaryVersionId == primary.Id)
                .Select(movie => (Guid?)movie.Id);
            return linked.Count > 0 && linked.SetEquals(alternates)
                && movies.All(movie => movie.PrimaryVersionId is null || movie.PrimaryVersionId == primary.Id);
        }

        private static bool IsUnlinked(Movie movie) =>
            movie.PrimaryVersionId is null && movie.LinkedAlternateVersions.Length == 0;

        private static string Normalize(string value) =>
            (value ?? string.Empty).Normalize(NormalizationForm.FormKC).ToUpperInvariant().TrimStart()
                .Replace('‐', '-').Replace('‑', '-').Replace('‒', '-').Replace('–', '-')
                .Replace('—', '-').Replace('−', '-');

        private static string GetCatalogKey(Match match)
        {
            var prefix = match.Groups["prefix"].Value;
            if (prefix.StartsWith("FC2", StringComparison.Ordinal))
            {
                prefix = prefix.Replace(" ", "").Replace("_", "").Replace("-", "");
                if (prefix == "FC2PPV")
                {
                    prefix = "FC2-PPV";
                }
            }

            return $"{prefix}-{match.Groups["number"].Value}";
        }

        public async Task SplitMoviesAsync(IProgress<double> progress, ClaimsPrincipal user = null, CancellationToken cancellationToken = default)
        {
            await MovieMergeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var groups = GetMovieMergeGroups(cancellationToken, splitting: true);
                var current = 0;
                foreach (var (_, movies) in groups)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var primary = movies.Single(movie => movie.LinkedAlternateVersions.Length > 0);
                    await _videoVersions.SplitAsync(primary.Id, user, cancellationToken).ConfigureAwait(false);
                    progress?.Report(++current / (double)groups.Count * 100);
                }

                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(100);
            }
            finally
            {
                MovieMergeLock.Release();
            }
        }

        private List<Movie> GetMoviesFromLibrary()
        {
            return _libraryManager
                    .GetItemList(
                        new InternalItemsQuery
                        {
                            IncludeItemTypes = [BaseItemKind.Movie],
                            IsVirtualItem = false,
                            Recursive = true,
                        }
                )
                .OfType<Movie>()
                .Where(IsEligible)
                .ToList();
        }

        private bool IsEligible(BaseItem item)
        {
            if (IsInInactiveLibrary(item) || IsInExcludedLibrary(item))
            {
                return false;
            }
            return true;
        }

        private bool IsInExcludedLibrary(BaseItem item)
        {
           return Plugin.Instance.PluginConfiguration.LocationsExcluded != null
                  && Plugin.Instance.PluginConfiguration.LocationsExcluded
                    .Any(s => _fileSystem.ContainsSubPath(s, item.Path));
        }

        private bool IsInInactiveLibrary(BaseItem item)
        {
            if (item is not Movie)
            {
                return false;
            }

            var parentPath = item.DisplayParent?.Path;
            if (string.IsNullOrWhiteSpace(parentPath))
            {
                return false;
            }

            var virtualFolders = _libraryManager.GetVirtualFolders();

            return !virtualFolders
                .SelectMany(vf => vf.Locations ?? Array.Empty<string>())
                .Any(libPath => string.Equals(libPath, parentPath, StringComparison.OrdinalIgnoreCase) ||
                                _fileSystem.ContainsSubPath(libPath, parentPath));
        }
        private void OnTimerElapsed() { }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer?.Dispose();
            }
        }
    }
}
