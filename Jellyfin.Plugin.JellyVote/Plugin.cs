using System;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyVote.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.JellyVote
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public const string PluginGuid = "d2cffc5c-a675-431a-add8-abb93d6aaf1f";

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public static Plugin? Instance { get; private set; }

        public override Guid Id => Guid.Parse(PluginGuid);

        public override string Name => "JellyVote";

        public override string Description =>
            "Community voting to delete movies, series, and music from your Jellyfin library.";

        public string BuildScriptTag()
        {
            var version = Version?.ToString() ?? "1.0.0.0";
            return $"<script plugin=\"JellyVote\" version=\"{version}\" src=\"../JellyVote/script\" defer></script>";
        }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            var ns = GetType().Namespace!;
            return new[]
            {
                new PluginPageInfo
                {
                    Name = Name,
                    EmbeddedResourcePath = ns + ".Configuration.configPage.html",
                    EnableInMainMenu = true,
                    MenuIcon = "how_to_vote"
                },
                new PluginPageInfo
                {
                    Name = "JellyVoteConfigCss",
                    EmbeddedResourcePath = ns + ".Configuration.configPage.css"
                }
            };
        }
    }
}
