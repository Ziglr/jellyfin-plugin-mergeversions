# Catalog Versions

A Jellyfin plugin for grouping OpenList STRM versions by catalog number. Forked from [Merge Versions](https://github.com/danieladov/jellyfin-plugin-mergeversions); it has an independent plugin ID, assembly, configuration, API route, and scheduled-task keys so both plugins can be installed.

Title and filename catalog numbers must agree after case, width, and separator normalization. Original, `-C`, `-U`, and `-UC` files are eligible. Conflicting titles, unknown/part suffixes, and conflicting version relationships prevent automatic merging. Outside the configured STRM directory, movie merging retains the upstream TMDb behavior.

## Install alongside Merge Versions

Use the **Catalog Versions** artifact from this fork's validation workflow. The old `Jellyfin.Plugin.MergeVersions.dll` artifact cannot provide side-by-side installation. Do not use the upstream manifest to install this fork.

1. Check the running Jellyfin version for compatibility: this build targets .NET 10 and references Jellyfin 12.1.0. Successful compilation alone does not verify compatibility with your installed server.
2. Inspect existing Merge Versions task triggers and pause them for the pilot. Record their previous settings.
3. Stop Jellyfin and back up its data/configuration outside the plugin directory.
4. Create `~/Library/Application Support/jellyfin/plugins/Catalog Versions_12.0.1.0/` on macOS and copy only `Jellyfin.Plugin.CatalogVersions.dll` into it. Keep the original Merge Versions installation in its own directory.
5. Restart Jellyfin and confirm both plugins load, with separate settings pages and no API-route errors.
6. Set Catalog Versions' STRM directory to `/Volumes/Storage/OpenList/strm`. New scheduled tasks have **no default triggers**. Do not enable a schedule yet.
7. Exclude the OpenList libraries from the original plugin. Review exclusions in Catalog Versions too: its batch task also retains TMDb merging outside the configured directory.
8. Preview `SHKD-988`; proceed only if one group contains the expected four source paths. Use **Merge This Catalog Group**.
9. Verify four media sources in the detail page, switch and play versions, and check actual Chinese subtitles on at least one `-C` version. A filename suffix alone does not prove subtitles work.
10. Only after the pilot passes, configure **Catalog Versions: Merge All Movies** in Jellyfin's scheduled tasks and record the displayed time/timezone. Restore original-plugin schedules only after confirming non-overlapping scope.

Plugin ID: `25bfeb4e-673e-4969-a92c-db3d990a3a80`. API prefix: `CatalogVersions`. Settings and schedules from Merge Versions are intentionally not migrated.

To remove this fork, stop Jellyfin and move its plugin folder outside `plugins`, then restart. Removing the DLL does not undo previously merged library relationships; restore a data backup or use Jellyfin's native version-management controls as appropriate.

## Build and validate

```sh
dotnet test Jellyfin.Plugin.CatalogVersions.sln --configuration Release
dotnet publish Jellyfin.Plugin.CatalogVersions/Jellyfin.Plugin.CatalogVersions.csproj --configuration Release --output plugin-build
```

Only the plugin DLL is installed. No SDK is required on the Jellyfin host. Merge and split use Jellyfin's native video-version actions; manual operations require administrator permissions. Tests do not replace live installation, playback, and scheduling checks.
