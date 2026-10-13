namespace Jellyfin.Plugin.HomeScreenCleanup.Configuration;

/// <summary>
/// Reads the live plugin configuration (test seam over <see cref="Plugin.Instance"/>).
/// </summary>
public interface ISettingsAccessor
{
    /// <summary>
    /// Gets the current configuration; never <see langword="null"/>.
    /// </summary>
    PluginConfiguration Current { get; }
}
