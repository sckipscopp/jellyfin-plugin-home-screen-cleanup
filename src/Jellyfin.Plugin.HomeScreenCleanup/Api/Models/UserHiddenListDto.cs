using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.HomeScreenCleanup.Api.Models;

/// <summary>
/// One user's hidden list (admin view).
/// </summary>
public sealed class UserHiddenListDto
{
    /// <summary>
    /// Gets or sets the user id.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the user name, or <see langword="null"/> when the user no longer exists.
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// Gets or sets the hidden series.
    /// </summary>
    public IReadOnlyList<HiddenSeriesDto> Entries { get; set; } = [];
}
