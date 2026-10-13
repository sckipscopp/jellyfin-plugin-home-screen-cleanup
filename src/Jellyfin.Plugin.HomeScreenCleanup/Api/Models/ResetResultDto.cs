using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.HomeScreenCleanup.Api.Models;

/// <summary>
/// Response of <c>POST HomeScreenCleanup/ContinueWatching/{itemId}/Remove</c>.
/// </summary>
public sealed class ResetResultDto
{
    /// <summary>
    /// Gets or sets the user whose progress was reset.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the requested item id.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets the ids of every version whose resume position was zeroed.
    /// </summary>
    public IReadOnlyList<Guid> ResetItemIds { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether anything changed.
    /// </summary>
    public bool Changed { get; set; }
}
