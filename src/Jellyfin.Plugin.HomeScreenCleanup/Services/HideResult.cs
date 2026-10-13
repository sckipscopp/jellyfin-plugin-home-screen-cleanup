namespace Jellyfin.Plugin.HomeScreenCleanup.Services;

/// <summary>
/// Result of <see cref="IHiddenSeriesStore.Hide"/>.
/// </summary>
public sealed class HideResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HideResult"/> class.
    /// </summary>
    /// <param name="outcome">What happened.</param>
    /// <param name="entry">The stored entry, or <see langword="null"/> when the limit was reached.</param>
    public HideResult(HideOutcome outcome, HiddenSeriesEntry? entry)
    {
        Outcome = outcome;
        Entry = entry;
    }

    /// <summary>
    /// Gets what happened.
    /// </summary>
    public HideOutcome Outcome { get; }

    /// <summary>
    /// Gets the stored entry (new or pre-existing); <see langword="null"/> for <see cref="HideOutcome.LimitReached"/>.
    /// </summary>
    public HiddenSeriesEntry? Entry { get; }
}
