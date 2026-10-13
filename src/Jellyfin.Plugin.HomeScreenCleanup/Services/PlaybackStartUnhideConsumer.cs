using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Jellyfin.Plugin.HomeScreenCleanup.Configuration;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HomeScreenCleanup.Services;

/// <summary>
/// Unhides a series from Next Up when a user starts playing any of its episodes. Never throws.
/// </summary>
public sealed class PlaybackStartUnhideConsumer : IEventConsumer<PlaybackStartEventArgs>
{
    private const string Reason = "playback";

    private readonly IHiddenSeriesStore _store;
    private readonly ISettingsAccessor _settings;
    private readonly ILogger<PlaybackStartUnhideConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackStartUnhideConsumer"/> class.
    /// </summary>
    /// <param name="store">The hidden-series store.</param>
    /// <param name="settings">The settings accessor.</param>
    /// <param name="logger">The logger.</param>
    public PlaybackStartUnhideConsumer(IHiddenSeriesStore store, ISettingsAccessor settings, ILogger<PlaybackStartUnhideConsumer> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "An event consumer must never break playback start.")]
    public Task OnEvent(PlaybackStartEventArgs eventArgs)
    {
        try
        {
            if (eventArgs is null || !_settings.Current.AutoUnhideOnPlayback)
            {
                return Task.CompletedTask;
            }

            if (eventArgs.Item is Episode episode && episode.SeriesId != Guid.Empty && eventArgs.Users is { } users)
            {
                var seriesId = episode.SeriesId;
                foreach (var user in users)
                {
                    var userId = user.Id;
                    if (_store.Unhide(userId, seriesId))
                    {
                        _logger.SeriesUnhidden(seriesId, userId, Reason);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.PlaybackUnhideFailed(ex);
        }

        return Task.CompletedTask;
    }
}
