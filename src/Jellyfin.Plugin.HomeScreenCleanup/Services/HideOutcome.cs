namespace Jellyfin.Plugin.HomeScreenCleanup.Services;

/// <summary>
/// Result kind of <see cref="IHiddenSeriesStore.Hide"/>.
/// </summary>
public enum HideOutcome
{
    /// <summary>
    /// The series was added to the user's hidden list.
    /// </summary>
    Added,

    /// <summary>
    /// The series was already hidden; the original entry is kept.
    /// </summary>
    AlreadyHidden,

    /// <summary>
    /// The user's hidden list is full; nothing changed.
    /// </summary>
    LimitReached,
}
