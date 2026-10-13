using System;
using System.IO;
using Jellyfin.Plugin.HomeScreenCleanup.Configuration;
using Jellyfin.Plugin.HomeScreenCleanup.Services;
using Jellyfin.Plugin.HomeScreenCleanup.Web;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HomeScreenCleanup;

/// <summary>
/// Registers the plugin's services with the server container (runs before <c>Startup.ConfigureServices</c>).
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        serviceCollection.AddSingleton<ISettingsAccessor, PluginSettingsAccessor>();
        serviceCollection.AddSingleton<IBaseUrlProvider, BaseUrlProvider>();
        serviceCollection.AddSingleton<IHiddenSeriesStore>(provider => new HiddenSeriesStore(
            ResolveDataFolder(provider),
            provider.GetRequiredService<ILogger<HiddenSeriesStore>>()));
        serviceCollection.AddSingleton<ResumeResetService>();

        serviceCollection.AddSingleton<NextUpResultFilter>();
        serviceCollection.PostConfigure<MvcOptions>(options => options.Filters.AddService<NextUpResultFilter>());

        serviceCollection.AddScoped<IEventConsumer<PlaybackStartEventArgs>, PlaybackStartUnhideConsumer>();
        serviceCollection.AddTransient<IStartupFilter, IndexInjectionStartupFilter>();
    }

    private static string ResolveDataFolder(IServiceProvider provider)
    {
        // DataFolderPath is unversioned (plugins\Jellyfin.Plugin.HomeScreenCleanup) and never created by the server.
        return Plugin.Instance?.DataFolderPath
            ?? Path.Combine(provider.GetRequiredService<IApplicationPaths>().PluginsPath, PluginConstants.DataFolderName);
    }
}
