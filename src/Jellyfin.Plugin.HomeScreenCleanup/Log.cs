using System;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HomeScreenCleanup;

/// <summary>
/// Source-generated log messages. Only GUIDs, counts and file paths are logged; never names or tokens.
/// </summary>
internal static partial class Log
{
    /// <summary>Logs that the hidden-series store was loaded.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The store file path.</param>
    /// <param name="userCount">Number of users with entries.</param>
    /// <param name="entryCount">Total number of entries.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "HomeScreenCleanup StoreLoaded {Path}: {UserCount} users, {EntryCount} entries")]
    public static partial void StoreLoaded(this ILogger logger, string path, int userCount, int entryCount);

    /// <summary>Logs that the store file was unreadable and was moved aside.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The read or parse failure.</param>
    /// <param name="path">The store file path.</param>
    /// <param name="corruptPath">Where the unreadable file was moved.</param>
    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "HomeScreenCleanup store {Path} was unreadable; moved to {CorruptPath} and starting empty")]
    public static partial void StoreCorrupt(this ILogger logger, Exception exception, string path, string corruptPath);

    /// <summary>Logs that an unreadable store file could not be moved aside.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The move failure.</param>
    /// <param name="path">The store file path.</param>
    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "HomeScreenCleanup store {Path} was unreadable and could not be moved aside; starting empty")]
    public static partial void StoreCorruptMoveFailed(this ILogger logger, Exception exception, string path);

    /// <summary>Logs that a stale temporary store file could not be removed.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The delete failure.</param>
    /// <param name="path">The temporary file path.</param>
    [LoggerMessage(EventId = 4, Level = LogLevel.Debug, Message = "HomeScreenCleanup could not remove stale temp file {Path}")]
    public static partial void StaleTempRemoveFailed(this ILogger logger, Exception exception, string path);

    /// <summary>Logs that a series was hidden from Next Up.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="seriesId">The series id.</param>
    /// <param name="userId">The user id.</param>
    [LoggerMessage(EventId = 10, Level = LogLevel.Information, Message = "HomeScreenCleanup Hidden series {SeriesId} from Next Up for user {UserId}")]
    public static partial void SeriesHidden(this ILogger logger, Guid seriesId, Guid userId);

    /// <summary>Logs that a series was unhidden from Next Up.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="seriesId">The series id.</param>
    /// <param name="userId">The user id.</param>
    /// <param name="reason">Why: user, admin or playback.</param>
    [LoggerMessage(EventId = 11, Level = LogLevel.Information, Message = "HomeScreenCleanup Unhidden series {SeriesId} for user {UserId} Reason={Reason}")]
    public static partial void SeriesUnhidden(this ILogger logger, Guid seriesId, Guid userId, string reason);

    /// <summary>Logs that an administrator cleared a user's hidden list.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="userId">The user id.</param>
    [LoggerMessage(EventId = 12, Level = LogLevel.Information, Message = "HomeScreenCleanup Cleared hidden list for user {UserId}")]
    public static partial void UserCleared(this ILogger logger, Guid userId);

    /// <summary>Logs a resume-position reset.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="itemId">The requested item id.</param>
    /// <param name="userId">The user id.</param>
    /// <param name="count">Number of versions whose position was reset.</param>
    [LoggerMessage(EventId = 20, Level = LogLevel.Information, Message = "HomeScreenCleanup Reset resume position of item {ItemId} for user {UserId}: {Count} version(s) changed")]
    public static partial void ResumeReset(this ILogger logger, Guid itemId, Guid userId, int count);

    /// <summary>Logs that the Next Up filter failed and the original result was kept.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(EventId = 30, Level = LogLevel.Warning, Message = "HomeScreenCleanup Next Up filter failed; returning the unfiltered result")]
    public static partial void NextUpFilterFailed(this ILogger logger, Exception exception);

    /// <summary>Logs that index.html could not be rewritten and the original bytes were sent.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(EventId = 40, Level = LogLevel.Warning, Message = "HomeScreenCleanup index.html injection failed; serving the original page")]
    public static partial void InjectionFailed(this ILogger logger, Exception exception);

    /// <summary>Logs that the server base URL could not be read.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(EventId = 41, Level = LogLevel.Warning, Message = "HomeScreenCleanup could not read the server base URL; matching index paths by suffix")]
    public static partial void BaseUrlUnavailable(this ILogger logger, Exception exception);

    /// <summary>Logs that the script tag was injected.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="length">The rewritten body length in bytes.</param>
    [LoggerMessage(EventId = 42, Level = LogLevel.Debug, Message = "HomeScreenCleanup injected client script into index.html ({Length} bytes)")]
    public static partial void IndexInjected(this ILogger logger, int length);

    /// <summary>Logs that the auto-unhide on playback failed.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(EventId = 50, Level = LogLevel.Warning, Message = "HomeScreenCleanup auto-unhide on playback failed")]
    public static partial void PlaybackUnhideFailed(this ILogger logger, Exception exception);
}
