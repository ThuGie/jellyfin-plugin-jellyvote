using Jellyfin.Plugin.JellyVote.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.JellyVote
{
    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            serviceCollection.AddSingleton<VoteStore>();
            serviceCollection.AddSingleton<AuditLogService>();
            serviceCollection.AddSingleton<NotificationService>();
            serviceCollection.AddSingleton<QuorumService>();
            serviceCollection.AddSingleton<DeletionService>();
            serviceCollection.AddSingleton<VoteWorkflowService>();
            serviceCollection.AddSingleton<IStartupFilter, ScriptInjectionStartupFilter>();
        }
    }
}
