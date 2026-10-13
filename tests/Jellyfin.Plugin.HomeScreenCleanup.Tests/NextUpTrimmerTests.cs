using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.HomeScreenCleanup.Services;
using MediaBrowser.Model.Dto;
using Xunit;

namespace Jellyfin.Plugin.HomeScreenCleanup.Tests;

public sealed class NextUpTrimmerTests
{
    [Fact]
    public void Window_without_limit_fetches_everything_from_zero()
    {
        var window = NextUpTrimmer.ComputeWindow(7, null, 3);

        Assert.Equal(0, window.StartIndex);
        Assert.Null(window.Limit);
    }

    [Fact]
    public void Window_over_fetches_start_plus_limit_plus_hidden()
    {
        var window = NextUpTrimmer.ComputeWindow(5, 10, 3);

        Assert.Equal(0, window.StartIndex);
        Assert.Equal(18, window.Limit);
    }

    [Fact]
    public void Window_is_capped_and_ignores_negative_inputs()
    {
        Assert.Equal(NextUpTrimmer.MaxFetch, NextUpTrimmer.ComputeWindow(int.MaxValue, int.MaxValue, int.MaxValue).Limit);
        Assert.Equal(4, NextUpTrimmer.ComputeWindow(-5, 4, -1).Limit);
        Assert.Equal(25, NextUpTrimmer.ComputeWindow(null, 24, 1).Limit);
    }

    [Fact]
    public void Trim_drops_hidden_series_then_pages()
    {
        var series = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToArray();
        var items = series.Select(Episode).ToArray();
        var hidden = new HashSet<Guid> { series[1], series[3] };

        var page = NextUpTrimmer.Trim(items, hidden.Contains, 2, 3, enableTotalRecordCount: true, originalTotal: 20);

        Assert.Equal(new[] { series[4], series[5], series[6] }, page.Items.Select(i => i.SeriesId!.Value));
        Assert.Equal(18, page.TotalRecordCount);
    }

    [Fact]
    public void Trim_reports_page_length_when_total_not_requested()
    {
        var series = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        var items = series.Select(Episode).ToArray();
        var hidden = new HashSet<Guid> { series[0] };

        var page = NextUpTrimmer.Trim(items, hidden.Contains, null, 24, enableTotalRecordCount: false, originalTotal: 0);

        Assert.Equal(4, page.Items.Count);
        Assert.Equal(4, page.TotalRecordCount);
    }

    [Fact]
    public void Trim_keeps_items_without_series_and_honours_limit_one()
    {
        var hiddenSeries = Guid.NewGuid();
        var items = new[]
        {
            Episode(hiddenSeries),
            new BaseItemDto { Id = Guid.NewGuid(), SeriesId = null },
            Episode(Guid.NewGuid()),
        };

        var page = NextUpTrimmer.Trim(items, id => id == hiddenSeries, 0, 1, enableTotalRecordCount: true, originalTotal: 3);

        Assert.Single(page.Items);
        Assert.Null(page.Items[0].SeriesId);
        Assert.Equal(2, page.TotalRecordCount);
    }

    [Fact]
    public void Trim_never_reports_negative_totals()
    {
        var hiddenSeries = Guid.NewGuid();

        var page = NextUpTrimmer.Trim([Episode(hiddenSeries)], id => id == hiddenSeries, 0, 10, enableTotalRecordCount: true, originalTotal: 0);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalRecordCount);
    }

    private static BaseItemDto Episode(Guid seriesId) => new() { Id = Guid.NewGuid(), SeriesId = seriesId };
}
