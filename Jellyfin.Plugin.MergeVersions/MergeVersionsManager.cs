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
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MergeVersions
{
    public class MergeVersionsManager : IDisposable
    {
        private static readonly Regex CatalogNumber = new(
            @"^(?:(?<prefix>FC2[ _-]*PPV)[ _-]*(?<number>[0-9]{4,10})|(?<prefix>[A-Z][A-Z0-9]{1,19})[ _-]+(?<number>[0-9]{2,9})|(?<prefix>[A-Z]{2,20})(?<number>[0-9]{2,9}))(?![A-Z0-9])",
            RegexOptions.CultureInvariant);
        private static readonly Regex VersionSuffix = new(@"^(?:[ _-]+(?:UC|C|U))?$", RegexOptions.CultureInvariant);
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

        public async Task MergeMoviesAsync(IProgress<double> progress, ClaimsPrincipal user = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await MovieMergeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _logger.LogInformation("Scanning for repeated movies");
                var duplicateMovies = GetMovieMergeGroups(cancellationToken);

                var current = 0;
                foreach (var (key, movies) in duplicateMovies)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var first = movies[0];
                    _logger.LogInformation("Merging {Key}: {Name} ({Year})", key, first.Name, first.ProductionYear);
                    await _videoVersions.MergeAsync(movies.Select(movie => movie.Id).ToArray(), user, cancellationToken).ConfigureAwait(false);
                    progress?.Report(++current / (double)duplicateMovies.Count * 100);
                }

                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(100);
            }
            finally
            {
                MovieMergeLock.Release();
            }
        }

        internal List<(string Key, Movie[] Movies)> GetMovieMergeGroups(CancellationToken cancellationToken)
        {
            var root = Plugin.Instance.PluginConfiguration.CatalogNumberRoot?.Trim();
            var useCatalog = !string.IsNullOrEmpty(root) && Path.IsPathFullyQualified(root);
            var catalog = new List<(string Key, Movie Movie, bool Matches)>();
            var standard = new List<Movie>();
            foreach (var movie in GetMoviesFromLibrary())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (useCatalog && !string.IsNullOrEmpty(movie.Path)
                    && string.Equals(Path.GetExtension(movie.Path), ".strm", StringComparison.OrdinalIgnoreCase)
                    && _fileSystem.ContainsSubPath(root, movie.Path))
                {
                    var filename = Normalize(Path.GetFileNameWithoutExtension(movie.Path));
                    var file = CatalogNumber.Match(filename);
                    if (file.Success)
                    {
                        var key = GetCatalogKey(file);
                        var title = CatalogNumber.Match(Normalize(movie.Name));
                        catalog.Add((key, movie, title.Success && GetCatalogKey(title) == key
                            && VersionSuffix.IsMatch(filename[file.Length..])));
                    }
                }
                else if (movie.ProviderIds.ContainsKey("Tmdb"))
                {
                    standard.Add(movie);
                }
            }

            var groups = standard
                .GroupBy(movie => movie.ProviderIds["Tmdb"])
                .Where(group => group.Count() > 1 && group.Any(IsUnlinked))
                .Select(group => ($"tmdb:{group.Key}", group.ToArray()))
                .ToList();
            groups.AddRange(catalog.GroupBy(item => item.Key, StringComparer.Ordinal)
                .Where(group => group.All(item => item.Matches))
                .Select(group => (Key: group.Key, Movies: group.Select(item => item.Movie).ToArray()))
                .Where(group => CanMergeCatalogGroup(group.Movies))
                .OrderBy(group => group.Key, StringComparer.Ordinal));
            return groups;
        }

        private static bool CanMergeCatalogGroup(Movie[] movies)
        {
            if (movies.Length < 2 || !movies.Any(IsUnlinked)
                || movies.Any(movie => movie.IsStacked || movie.LocalAlternateVersions.Length > 0))
            {
                return false;
            }

            var primaries = movies.Where(movie => movie.LinkedAlternateVersions.Length > 0).ToArray();
            if (primaries.Length == 0)
            {
                return movies.All(IsUnlinked);
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
            cancellationToken.ThrowIfCancellationRequested();
            var movies = GetMoviesFromLibrary().Where(movie => movie.ProviderIds.ContainsKey("Tmdb")).ToList();
            var current = 0;
            foreach (var movie in movies)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger.LogInformation("Splitting {Name} ({Year})", movie.Name, movie.ProductionYear);
                await _videoVersions.SplitAsync(movie.Id, user, cancellationToken).ConfigureAwait(false);
                progress?.Report(++current / (double)movies.Count * 100);
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(100);
        }

        public async Task MergeEpisodesAsync(IProgress<double> progress, ClaimsPrincipal user = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogInformation("Scanning for repeated episodes");

            var episodes = GetEpisodesFromLibrary();
            var duplicateEpisodes = episodes
                .GroupBy(GetEpisodeMergeKey, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1 &&
                                group.Any(episode => episode.PrimaryVersionId == null &&
                                                     !episode.LinkedAlternateVersions.Any()))
                .ToList();

            _logger.LogInformation(
                "Found {EpisodeCount} episodes and {DuplicateGroupCount} duplicate episode groups",
                episodes.Count,
                duplicateEpisodes.Count);

            var current = 0;
            foreach (var episodeGroup in duplicateEpisodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var first = episodeGroup.First();
                _logger.LogInformation("Merging {Name} ({Year})", first.Name, first.ProductionYear);
                await _videoVersions.MergeAsync(episodeGroup.Select(e => e.Id).ToArray(), user, cancellationToken).ConfigureAwait(false);
                progress?.Report(++current / (double)duplicateEpisodes.Count * 100);
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(100);
        }

        public async Task SplitEpisodesAsync(IProgress<double> progress, ClaimsPrincipal user = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var episodes = GetEpisodesFromLibrary();
            var current = 0;

            foreach (var episode in episodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger.LogInformation("Splitting {EpisodeNumber} ({SeriesName})", episode.IndexNumber, episode.SeriesName);
                await _videoVersions.SplitAsync(episode.Id, user, cancellationToken).ConfigureAwait(false);
                progress?.Report(++current / (double)episodes.Count * 100);
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(100);
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

        private List<Episode> GetEpisodesFromLibrary()
        {
            return _libraryManager
                .GetItemList(
                    new InternalItemsQuery
                    {
                        IncludeItemTypes = [BaseItemKind.Episode],
                        IsVirtualItem = false,
                        Recursive = true,
                    }
                )
                .Select(m => m as Episode)
                .Where(IsEligible)
                .ToList();
        }

        private static string GetEpisodeMergeKey(Episode episode)
        {
            if (episode.ParentIndexNumber.HasValue && episode.IndexNumber.HasValue)
            {
                return $"number:{episode.SeriesName}:{episode.ParentIndexNumber}:{episode.IndexNumber}:{episode.IndexNumberEnd}";
            }

            foreach (var provider in new[] { "Tvdb", "Tmdb", "Imdb" })
            {
                if (episode.ProviderIds.TryGetValue(provider, out var providerId)
                    && !string.IsNullOrWhiteSpace(providerId))
                {
                    return $"provider:{episode.SeriesName}:{provider}:{providerId}";
                }
            }

            return $"title:{episode.SeriesName}:{episode.SeasonName}:{episode.Name}:{episode.ProductionYear}";
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
