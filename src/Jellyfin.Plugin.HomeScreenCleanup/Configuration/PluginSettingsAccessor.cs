namespace Jellyfin.Plugin.HomeScreenCleanup.Configuration;

/// <summary>
/// Reads the configuration from the running plugin instance, falling back to defaults.
/// </summary>
public sealed class PluginSettingsAccessor : ISettingsAccessor
{
    /// <inheritdoc />
    public PluginConfiguration Current => Plugin.Instance?.Configuration ?? new PluginConfiguration();
}
