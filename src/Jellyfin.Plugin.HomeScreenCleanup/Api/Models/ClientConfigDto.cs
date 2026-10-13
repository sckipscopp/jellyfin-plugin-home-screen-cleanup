namespace Jellyfin.Plugin.HomeScreenCleanup.Api.Models;

/// <summary>
/// Feature toggles the client script reads at start-up.
/// </summary>
public sealed class ClientConfigDto
{
    /// <summary>
    /// Gets or sets a value indicating whether "Remove from Continue Watching" is offered.
    /// </summary>
    public bool EnableContinueWatchingAction { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether "Remove from Next Up" is offered.
    /// </summary>
    public bool EnableNextUpAction { get; set; }
}
