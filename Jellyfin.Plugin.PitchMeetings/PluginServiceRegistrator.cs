using Jellyfin.Plugin.PitchMeetings.Injection;
using Jellyfin.Plugin.PitchMeetings.Matching;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.PitchMeetings;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IStartupFilter, ScriptInjectionStartupFilter>();
        serviceCollection.AddSingleton<PitchIndex>();
    }
}
