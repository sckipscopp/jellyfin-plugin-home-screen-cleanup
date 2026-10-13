namespace Jellyfin.Plugin.HomeScreenCleanup.Services;

/// <summary>
/// The paging window the Next Up filter asks the server for.
/// </summary>
public sealed class NextUpWindow
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NextUpWindow"/> class.
    /// </summary>
    /// <param name="startIndex">The start index to request.</param>
    /// <param name="limit">The limit to request, or <see langword="null"/> for no limit.</param>
    public NextUpWindow(int startIndex, int? limit)
    {
        StartIndex = startIndex;
        Limit = limit;
    }

    /// <summary>
    /// Gets the start index to request.
    /// </summary>
    public int StartIndex { get; }

    /// <summary>
    /// Gets the limit to request, or <see langword="null"/> for no limit.
    /// </summary>
    public int? Limit { get; }
}
