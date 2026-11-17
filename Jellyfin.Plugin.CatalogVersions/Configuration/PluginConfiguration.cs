using MediaBrowser.Model.Plugins;
using System;

namespace Jellyfin.Plugin.CatalogVersions.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {

        public String[] LocationsExcluded { get; set; }

        public string CatalogNumberRoot { get; set; } = string.Empty;

        public PluginConfiguration()
        {
            LocationsExcluded = Array.Empty<String>();
        }
    }
}
