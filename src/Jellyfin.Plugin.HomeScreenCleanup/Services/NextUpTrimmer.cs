using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Dto;

namespace Jellyfin.Plugin.HomeScreenCleanup.Services;

/// <summary>
/// Pure paging math for the Next Up filter: over-fetch from index 0, drop hidden series, re-page.
/// </summary>
internal static class NextUpTrimmer
{
    /// <summary>
    /// Upper bound on the over-fetched window.
    /// </summary>
    public const int MaxFetch = 10_000;

    /// <summary>
    /// Computes the window to request so that removing up to <paramref name="hiddenCount"/> items still fills the page.
    /// </summary>
    /// <param name="startIndex">The client's start index.</param>
    /// <param name="limit">The client's limit, or <see langword="null"/> for no limit.</param>
    /// <param name="hiddenCount">Number of hidden series for the user.</param>
    /// <returns>The window to request.</returns>
    public static NextUpWindow ComputeWindow(int? startIndex, int? limit, int hiddenCount)
    {
        if (limit is null)
        {
            return new NextUpWindow(0, null);
        }

        long start = Math.Max(0, startIndex ?? 0);
        long wanted = start + Math.Max(0, limit.Value) + Math.Max(0, hiddenCount);
        return new NextUpWindow(0, (int)Math.Min(wanted, MaxFetch));
    }

    /// <summary>
    /// Removes hidden series from an over-fetched window and cuts the client's page out of it.
    /// </summary>
    /// <param name="items">The items returned for the window computed by <see cref="ComputeWindow"/>.</param>
    /// <param name="isHidden">Returns <see langword="true"/> for a hidden series id.</param>
    /// <param name="originalStartIndex">The client's start index.</param>
    /// <param name="originalLimit">The client's limit.</param>
    /// <param name="enableTotalRecordCount">Whether the client asked for a total record count.</param>
    /// <param name="originalTotal">The total record count the server reported for the window request.</param>
    /// <returns>The page and the total to report.</returns>
    public static NextUpPage Trim(
        IReadOnlyList<BaseItemDto> items,
        Func<Guid, bool> isHidden,
        int? originalStartIndex,
        int? originalLimit,
        bool enableTotalRecordCount,
        int originalTotal)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(isHidden);

        var visible = items
            .Where(item => item is not null && !(item.SeriesId is { } seriesId && seriesId != Guid.Empty && isHidden(seriesId)))
            .ToList();
        var removed = items.Count - visible.Count;

        IEnumerable<BaseItemDto> page = visible.Skip(Math.Max(0, originalStartIndex ?? 0));
        if (originalLimit is { } limit)
        {
            page = page.Take(Math.Max(0, limit));
        }

        var pageItems = page.ToArray();
        var total = enableTotalRecordCount ? Math.Max(0, originalTotal - removed) : pageItems.Length;
        return new NextUpPage(pageItems, total);
    }
}
