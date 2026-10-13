using System.Collections.Generic;
using MediaBrowser.Model.Dto;

namespace Jellyfin.Plugin.HomeScreenCleanup.Services;

/// <summary>
/// A trimmed Next Up page.
/// </summary>
public sealed class NextUpPage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NextUpPage"/> class.
    /// </summary>
    /// <param name="items">The page items.</param>
    /// <param name="totalRecordCount">The total record count to report.</param>
    public NextUpPage(IReadOnlyList<BaseItemDto> items, int totalRecordCount)
    {
        Items = items;
        TotalRecordCount = totalRecordCount;
    }

    /// <summary>
    /// Gets the page items.
    /// </summary>
    public IReadOnlyList<BaseItemDto> Items { get; }

    /// <summary>
    /// Gets the total record count to report.
    /// </summary>
    public int TotalRecordCount { get; }
}
