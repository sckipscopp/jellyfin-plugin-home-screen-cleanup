using System;

namespace Jellyfin.Plugin.HomeScreenCleanup.Services;

/// <summary>
/// One series a user hid from Next Up. Treated as immutable once stored; persisted as-is in <c>hidden-series.json</c>.
/// </summary>
public sealed class HiddenSeriesEntry
{
    /// <summary>
    /// Gets or sets the user id.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the series id.
    /// </summary>
    public Guid SeriesId { get; set; }

    /// <summary>
    /// Gets or sets the series name at the time it was hidden (display only).
    /// </summary>
    public string? SeriesName { get; set; }

    /// <summary>
    /// Gets or sets when the series was first hidden (UTC).
    /// </summary>
    public DateTimeOffset HiddenAtUtc { get; set; }
}
