using System;
using System.Collections.Generic;
using System.IO;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using IntroDbApp.Configuration;

namespace IntroDbApp
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages, IHasThumbImage
    {
        public static Plugin? Instance { get; private set; }

        public override string Name => "IntroDB (introdb.app)";
        public override string Description => "Complemento autónomo e híbrido v4.2.2 para la detección y salto universal de intros, créditos y escenas post-créditos mediante IntroDB.app.";
        public override Guid Id => Guid.Parse("f62e84d1-817a-4c48-8cf9-a6f951e73792");

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "introdb",
                    EmbeddedResourcePath = GetType().Namespace + ".Web.introdb.html",
                    EnableInMainMenu = true,
                    MenuSection = "advanced",
                    MenuIcon = "fast_forward",
                    DisplayName = "IntroDB.app"
                },
                new PluginPageInfo
                {
                    Name = "introdbjs",
                    EmbeddedResourcePath = GetType().Namespace + ".Web.introdb.js"
                },
                new PluginPageInfo
                {
                    Name = "introdbjs_v42",
                    EmbeddedResourcePath = GetType().Namespace + ".Web.introdb.js"
                },
                new PluginPageInfo
                {
                    Name = "introdbjs_v421",
                    EmbeddedResourcePath = GetType().Namespace + ".Web.introdb.js"
                },
                new PluginPageInfo
                {
                    Name = "introdbjs_v422",
                    EmbeddedResourcePath = GetType().Namespace + ".Web.introdb.js"
                }
            };
        }

        public Stream GetThumbImage()
        {
            var type = GetType();
            return type.Assembly.GetManifestResourceStream(type.Namespace + ".thumb.png")!;
        }

        public ImageFormat ThumbImageFormat => ImageFormat.Png;
    }
}