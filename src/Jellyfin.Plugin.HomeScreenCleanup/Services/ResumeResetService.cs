using System;
using System.Collections.Generic;
using System.Threading;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using JellyfinUser = Jellyfin.Database.Implementations.Entities.User;

namespace Jellyfin.Plugin.HomeScreenCleanup.Services;

/// <summary>
/// Removes an item from Continue Watching by marking the item and every alternate version played
/// (which also zeroes the resume position), so it neither resumes nor surfaces in Next Up.
/// </summary>
public sealed class ResumeResetService
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;
    private readonly ILogger<ResumeResetService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResumeResetService"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="userDataManager">The user data manager.</param>
    /// <param name="logger">The logger.</param>
    public ResumeResetService(ILibraryManager libraryManager, IUserDataManager userDataManager, ILogger<ResumeResetService> logger)
    {
        ArgumentNullException.ThrowIfNull(libraryManager);
        ArgumentNullException.ThrowIfNull(userDataManager);
        ArgumentNullException.ThrowIfNull(logger);
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
        _logger = logger;
    }

    /// <summary>
    /// Marks <paramref name="video"/> and all of its versions played for <paramref name="user"/>, resetting their resume position.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="video">The requested video (any version).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Ids of the versions that were marked played (empty when every version already was, with no progress).</returns>
    public IReadOnlyList<Guid> ResetProgress(JellyfinUser user, Video video, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(video);

        var reset = new List<Guid>();
        foreach (var version in EnumerateVersions(video))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var data = _userDataManager.GetUserData(user, version);
            if (data is { Played: true, PlaybackPositionTicks: <= 0 })
            {
                continue;
            }

            // BaseItem.MarkPlayed saves the user data itself (reason TogglePlayed), which also triggers the
            // UserDataChanged websocket push that refreshes the web client's home rows.
            version.MarkPlayed(user, DateTime.UtcNow, resetPosition: true);
            reset.Add(version.Id);
        }

        var itemId = video.Id;
        var userId = user.Id;
        _logger.ResumeReset(itemId, userId, reset.Count);
        return reset;
    }

    /// <summary>
    /// Returns the primary version, every alternate version (linked and local) and the requested item itself, de-duplicated.
    /// </summary>
    /// <param name="video">The requested video.</param>
    /// <returns>The versions.</returns>
    private List<Video> EnumerateVersions(Video video)
    {
        var seen = new HashSet<Guid>();
        var versions = new List<Video>();

        // Jellyfin 12.2: PrimaryVersionId is Guid?; alternates are enumerated through ILibraryManager
        // (linked = separate items for the same content, local = other files beside the primary).
        var primary = video;
        if (video.PrimaryVersionId is Guid primaryId
            && _libraryManager.GetItemById<Video>(primaryId) is { } primaryVideo)
        {
            primary = primaryVideo;
        }

        if (seen.Add(primary.Id))
        {
            versions.Add(primary);
        }

        foreach (var linked in _libraryManager.GetLinkedAlternateVersions(primary))
        {
            if (linked is not null && seen.Add(linked.Id))
            {
                versions.Add(linked);
            }
        }

        foreach (var localId in _libraryManager.GetLocalAlternateVersionIds(primary))
        {
            if (_libraryManager.GetItemById<Video>(localId) is { } local && seen.Add(local.Id))
            {
                versions.Add(local);
            }
        }

        if (seen.Add(video.Id))
        {
            versions.Add(video);
        }

        return versions;
    }
}
