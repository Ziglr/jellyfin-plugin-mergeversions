using System.Security.Claims;
using Jellyfin.Plugin.CatalogVersions.Api;
using Jellyfin.Plugin.CatalogVersions.Configuration;
using Jellyfin.Plugin.CatalogVersions.ScheduledTasks;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.CatalogVersions.Tests;

public class MergeVersionsManagerTests
{
    [Fact]
    public void IndependentIdentityAndOptInSchedules()
    {
        var paths = new Mock<IServerApplicationPaths>();
        paths.SetupGet(p => p.PluginsPath).Returns(AppContext.BaseDirectory);
        var plugin = new TestPlugin(paths.Object);
        Assert.Equal("Catalog Versions", plugin.Name);
        Assert.Equal(Guid.Parse("25bfeb4e-673e-4969-a92c-db3d990a3a80"), plugin.Id);
        Assert.Equal("Jellyfin.Plugin.CatalogVersions", typeof(Plugin).Assembly.GetName().Name);
        Assert.Equal("Catalog Versions", Assert.Single(plugin.GetPages()).Name);
        Assert.Equal("CatalogVersions", Assert.Single(typeof(MergeVersionsController)
            .GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>()).Template);
        var movies = new MergeMoviesTask(null, NullLogger<MergeVersionsManager>.Instance);
        var episodes = new SplitMoviesTask(null, NullLogger<MergeVersionsManager>.Instance);
        Assert.Equal("CatalogVersions.MergeMovies", movies.Key);
        Assert.Equal("CatalogVersions.SplitMovies", episodes.Key);
        Assert.Empty(movies.GetDefaultTriggers());
        Assert.Empty(episodes.GetDefaultTriggers());
    }

    [Fact]
    public async Task BatchMergeProcessesMultipleGroupsWithinScope()
    {
        var first = MetadataMovie("ABC-123 Film", "one.strm", "");
        var second = MetadataMovie("ABC-123 Film", "two.strm", "");
        var third = MetadataMovie("DEF-456 Film", "three.strm", "");
        var fourth = MetadataMovie("DEF-456 Film", "four.strm", "");
        var outside = MetadataMovie(first.Name, "outside.strm", "");
        outside.Path = "/other/outside.strm";
        var excluded = MetadataMovie(first.Name, "excluded/source.strm", "");

        var calls = await MergeMetadataMovies(new[] { first, second, third, fourth, outside, excluded });
        Assert.Equal(2, calls.Count);
        Assert.Equal(new[] { first.Id, second.Id }, calls[0]);
        Assert.Equal(new[] { third.Id, fourth.Id }, calls[1]);
    }

    [Fact]
    public async Task BatchSplitOnlyTouchesPrimariesWithAllSourcesInScope()
    {
        var first = MetadataMovie("ABC-123 Film", "one.strm", "");
        var second = MetadataMovie(first.Name, "two.strm", "");
        first.LinkedAlternateVersions = new[] { new LinkedChild { ItemId = second.Id } };
        second.PrimaryVersionId = first.Id;

        var outsidePrimary = MetadataMovie("DEF-456 Film", "three.strm", "");
        var outsideAlternate = MetadataMovie(outsidePrimary.Name, "excluded/four.strm", "");
        outsidePrimary.LinkedAlternateVersions = new[] { new LinkedChild { ItemId = outsideAlternate.Id } };
        outsideAlternate.PrimaryVersionId = outsidePrimary.Id;

        var versions = new Mock<IVideoVersions>(MockBehavior.Strict);
        versions.Setup(v => v.SplitAsync(first.Id, null, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        using var manager = CreateMetadataManager(new[] { first, second, outsidePrimary, outsideAlternate }, versions);
        await new SplitMoviesTask(manager, NullLogger<MergeVersionsManager>.Instance).ExecuteAsync(null, default);
        Assert.Single(versions.Invocations);
    }

    [Theory]
    [InlineData("A translated title", "Another title", "JavBus:123", "JavBus:123", true)]
    [InlineData("Same title", "Same title", "JavBus:123", "Jav321:456", true)]
    [InlineData("300MIUM-1234 Title", "３００ＭＩＵＭ－１２３４ Translated", "FANZA:300mium01234", "JavBus:300MIUM-1234", true)]
    [InlineData("FC2-PPV-1234567 Title", "FC2 PPV 1234567 Other", "source:1", "source:2", true)]
    [InlineData("SUPD-063 Title", "SUPD063 Other", "FANZA:supd00063", "JavBus:SUPD-063", true)]
    [InlineData("300MIUM-1234 Title", "300MIUM-1235 Title", "source:1", "source:2", false)]
    [InlineData("ＳＵＰＤ－０６３ Title", "supd 063 Different title", "JavBus:123", "Jav321:456", true)]
    [InlineData("SUPD-063 Title", "SUPD-064 Title", "JavBus:123", "JavBus:123", false)]
    [InlineData("Different title", "Another title", "JavBus:123", "Jav321:123", false)]
    [InlineData("", "", "", "", false)]
    public async Task MetadataIdentityRules(string firstTitle, string secondTitle, string firstId, string secondId, bool merge)
    {
        var first = MetadataMovie(firstTitle, "arbitrary_1.strm", firstId);
        var second = MetadataMovie(secondTitle, "unrelated_2.strm", secondId);
        var calls = await MergeMetadataMovies(new[] { first, second });
        Assert.Equal(merge ? 1 : 0, calls.Count);
        if (merge) Assert.Equal(new[] { first.Id, second.Id }.Order(), calls.Single().Order());
    }

    [Fact]
    public async Task Supd063IncludesAllFourPartsAndWholeMovie()
    {
        var movies = Enumerable.Range(0, 5).Select(i => MetadataMovie("SUPD-063 DIGITAL CHANNEL", $"source_{i}.strm", "")).ToArray();
        var calls = await MergeMetadataMovies(movies);
        Assert.Equal(movies.Select(m => m.Id).Order(), Assert.Single(calls).Order());
    }

    [Fact]
    public async Task ConflictingCatalogsCannotBeBridgedThroughMissingCatalogMetadata()
    {
        var first = MetadataMovie("SUPD-063 Title", "1.strm", "source:1");
        var bridge = MetadataMovie("Missing catalog", "2.strm", "source:1");
        var bridge2 = MetadataMovie("Missing catalog", "3.strm", "source:2");
        var other = MetadataMovie("SUPD-064 Title", "4.strm", "source:2");
        // Selecting a single catalog must not hide a conflict elsewhere in the component.
        Assert.Empty(await MergeMetadataMovies(new[] { first, bridge, bridge2, other }));
    }

    [Fact]
    public async Task AlreadyLinkedMetadataGroupDoesNotMergeAgain()
    {
        var first = MetadataMovie("SUPD-063 Title", "1.strm", "");
        var second = MetadataMovie("SUPD-063 Title", "2.strm", "");
        first.LinkedAlternateVersions = new[] { new LinkedChild { ItemId = second.Id } };
        second.PrimaryVersionId = first.Id;
        Assert.Empty(await MergeMetadataMovies(new[] { first, second }));
    }

    private static Movie MetadataMovie(string title, string file, string id)
    {
        var movie = new Movie { Id = Guid.NewGuid(), Name = title, Path = "/catalog/" + file };
        if (id.Length > 0) movie.ProviderIds["MetaTube"] = id;
        return movie;
    }

    private static async Task<List<Guid[]>> MergeMetadataMovies(Movie[] movies)
    {
        var calls = new List<Guid[]>();
        var versions = new Mock<IVideoVersions>(MockBehavior.Strict);
        versions.Setup(v => v.MergeAsync(It.IsAny<Guid[]>(), It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .Callback<Guid[], ClaimsPrincipal, CancellationToken>((ids, user, ct) => calls.Add(ids)).Returns(Task.CompletedTask);
        using var manager = CreateMetadataManager(movies, versions);
        var controller = new MergeVersionsController(manager, NullLogger<MergeVersionsManager>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        Assert.IsType<NoContentResult>(await controller.MergeMoviesRequestAsync());
        return calls;
    }

    private static MergeVersionsManager CreateMetadataManager(Movie[] movies, Mock<IVideoVersions> versions)
    {
        var paths = new Mock<IServerApplicationPaths>();
        paths.SetupGet(p => p.PluginsPath).Returns(AppContext.BaseDirectory);
        var plugin = new TestPlugin(paths.Object);
        plugin.PluginConfiguration.CatalogNumberRoot = "/catalog";
        plugin.PluginConfiguration.LocationsExcluded = new[] { "/catalog/excluded" };
        var library = new Mock<ILibraryManager>(MockBehavior.Strict);
        library.Setup(l => l.GetItemList(It.IsAny<InternalItemsQuery>())).Returns(movies.Cast<BaseItem>().ToList());
        var files = new Mock<IFileSystem>();
        files.Setup(f => f.ContainsSubPath(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string parent, string path) => path.StartsWith(parent + "/", StringComparison.Ordinal));
        return new MergeVersionsManager(library.Object, NullLogger<MergeVersionsManager>.Instance, files.Object, versions.Object);
    }

    [Fact]
    public void ManualEntryPointsRequireElevation()
    {
        var authorization = Assert.Single(typeof(MergeVersionsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(Policies.RequiresElevation, authorization.Policy);
    }

    private sealed class TestPlugin : Plugin
    {
        public TestPlugin(IServerApplicationPaths paths) : base(paths, Mock.Of<IXmlSerializer>())
        {
            Configuration = new PluginConfiguration();
        }
    }
}
