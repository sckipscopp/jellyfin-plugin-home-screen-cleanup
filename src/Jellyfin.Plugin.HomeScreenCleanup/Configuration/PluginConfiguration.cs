using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.HomeScreenCleanup.Configuration;

/// <summary>
/// Plugin settings. XmlSerializer-backed by Jellyfin, so only simple types are allowed here.
/// </summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether the client script tag is injected into the web client's index.html.
    /// </summary>
    public bool EnableScriptInjection { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether playing any episode of a hidden series unhides it from Next Up.
    /// </summary>
    public bool AutoUnhideOnPlayback { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the client shows "Remove from Continue Watching".
    /// </summary>
    public bool EnableContinueWatchingAction { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the client shows "Remove from Next Up".
    /// </summary>
    public bool EnableNextUpAction { get; set; } = true;
}
