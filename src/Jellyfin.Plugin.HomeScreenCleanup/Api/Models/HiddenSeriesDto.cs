using System;

namespace Jellyfin.Plugin.HomeScreenCleanup.Api.Models;

/// <summary>
/// One series hidden from Next Up.
/// </summary>
public sealed class HiddenSeriesDto
{
    /// <summary>
    /// Gets or sets the series id.
    /// </summary>
    public Guid SeriesId { get; set; }

    /// <summary>
    /// Gets or sets the series name recorded when it was hidden.
    /// </summary>
    public string? SeriesName { get; set; }

    /// <summary>
    /// Gets or sets when the series was first hidden (UTC).
    /// </summary>
    public DateTimeOffset HiddenAtUtc { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the series was already hidden before this request.
    /// </summary>
    public bool AlreadyHidden { get; set; }
}
