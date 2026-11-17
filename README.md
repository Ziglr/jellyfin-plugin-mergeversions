# Catalog Versions

A Jellyfin plugin for grouping OpenList STRM versions by catalog number. Forked from [Merge Versions](https://github.com/danieladov/jellyfin-plugin-mergeversions); it has an independent plugin ID, assembly, configuration, API route, and scheduled-task keys so both plugins can be installed.

Within the configured directory, movie identity comes only from Jellyfin metadata: an exact full MetaTube ID, a normalized identical title, or the same normalized catalog number at the start of the title. Titles from different scrapers may differ when the catalog number agrees. Filenames, extensions and numbered-part suffixes do not determine identity. Parts can be grouped with full versions, but this does not concatenate them or provide sequential playback.

Different explicit title catalog numbers always block a connected metadata group, even if MetaTube IDs agree or records without a catalog number bridge the groups. Jellyfin logs these conflicts. Existing conflicting version relationships also prevent merging. Both operations are limited to movies inside the configured directory, minus excluded paths.

## Install alongside Merge Versions

Use the **Catalog Versions** artifact from a successful validation workflow for the revision you intend to install. The old `Jellyfin.Plugin.MergeVersions.dll` artifact cannot provide side-by-side installation. Do not use the upstream manifest to install this fork.

1. Check the running Jellyfin version for compatibility: this build targets .NET 10 and references Jellyfin 12.1.0. Successful compilation alone does not verify compatibility with your installed server.
2. Inspect existing Merge Versions task triggers and pause them for the pilot. Record their previous settings.
3. Stop Jellyfin and back up its data/configuration outside the plugin directory.
4. Create `~/Library/Application Support/jellyfin/plugins/Catalog Versions_12.0.3.0/` on macOS and copy only `Jellyfin.Plugin.CatalogVersions.dll` into it. Keep the original Merge Versions installation in its own directory.
5. Restart Jellyfin and confirm both plugins load, with separate settings pages and no API-route errors.
6. Set Catalog Versions' STRM directory to `/Volumes/Storage/OpenList/strm`. New scheduled tasks have **no default triggers**. Do not enable a schedule yet.
7. Set exclusions so Catalog Versions and Merge Versions process different libraries.
8. Use **合并 AV** to merge all eligible groups in the saved directory. Check a few known groups for the expected selectable sources and playback.
9. Use **拆分 AV** to unlink matching version groups whose linked sources are all in scope. This can also unlink eligible groups created manually or by other plugins.

Plugin ID: `25bfeb4e-673e-4969-a92c-db3d990a3a80`. API prefix: `CatalogVersions`. Settings and schedules from Merge Versions are intentionally not migrated.

To remove this fork, stop Jellyfin and move its plugin folder outside `plugins`, then restart. Removing the DLL does not undo previously merged library relationships; restore a data backup or use Jellyfin's native version-management controls as appropriate.

## Build and validate

```sh
dotnet test Jellyfin.Plugin.CatalogVersions.sln --configuration Release
dotnet publish Jellyfin.Plugin.CatalogVersions/Jellyfin.Plugin.CatalogVersions.csproj --configuration Release --output plugin-build
```

Only the plugin DLL is installed. No SDK is required on the Jellyfin host. Merge and split use Jellyfin's native video-version actions; manual operations require administrator permissions. Tests do not replace live installation, playback, and scheduling checks.
