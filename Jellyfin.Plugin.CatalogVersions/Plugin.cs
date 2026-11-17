using System;
using System.Collections.Generic;
using Jellyfin.Plugin.CatalogVersions.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Common.Plugins;

namespace Jellyfin.Plugin.CatalogVersions
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages 
    {
        public Plugin(IServerApplicationPaths appPaths, IXmlSerializer xmlSerializer)
            : base(appPaths, xmlSerializer)
        {
            Instance = this;
        }

        public override string Name => "Catalog Versions";

        public static Plugin Instance { get; private set; }

        public override string Description
            => "Catalog Versions";

        public PluginConfiguration PluginConfiguration => Configuration;

        private readonly Guid _id = new Guid("25bfeb4e-673e-4969-a92c-db3d990a3a80");
        public override Guid Id => _id;

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "Catalog Versions",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.configurationpage.html"
                }
            };
        }
    }
}
