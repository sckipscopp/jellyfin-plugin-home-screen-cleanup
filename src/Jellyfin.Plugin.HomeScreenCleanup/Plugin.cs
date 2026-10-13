using System;
using System.Collections.Generic;
using Jellyfin.Plugin.HomeScreenCleanup.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.HomeScreenCleanup;

/// <summary>
/// The Home Screen Cleanup plugin entry point.
/// </summary>
public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">The server application paths.</param>
    /// <param name="xmlSerializer">The XML serializer used for the plugin configuration.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>
    /// Gets the current plugin instance, or <see langword="null"/> before the server created it.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => PluginConstants.Name;

    /// <inheritdoc />
    public override Guid Id => PluginConstants.Id;

    /// <inheritdoc />
    public override string Description =>
        "Adds 'Remove from Continue Watching' and 'Remove from Next Up' to the home screen card menu.";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = "HomeScreenCleanup",
                EmbeddedResourcePath = PluginConstants.ConfigPageResourceName,
            },
        ];
    }
}
