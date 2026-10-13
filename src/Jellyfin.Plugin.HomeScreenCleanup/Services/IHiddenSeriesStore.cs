using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.HomeScreenCleanup.Services;

/// <summary>
/// Per-user list of series hidden from Next Up. Reads are lock-free; writes persist before they publish.
/// </summary>
public interface IHiddenSeriesStore
{
    /// <summary>
    /// Gets the user's hidden entries keyed by series id (empty when none).
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <returns>An immutable snapshot.</returns>
    IReadOnlyDictionary<Guid, HiddenSeriesEntry> GetUserEntries(Guid userId);

    /// <summary>
    /// Gets every entry of every user, ordered by user then hide time.
    /// </summary>
    /// <returns>An immutable snapshot.</returns>
    IReadOnlyList<HiddenSeriesEntry> ListAll();

    /// <summary>
    /// Hides a series for a user. Idempotent: an existing entry keeps its first timestamp.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="seriesId">The series id.</param>
    /// <param name="seriesName">The series name (display only).</param>
    /// <param name="nowUtc">The current time.</param>
    /// <returns>The outcome and the stored entry.</returns>
    /// <exception cref="System.IO.IOException">The store file could not be written; nothing changed.</exception>
    HideResult Hide(Guid userId, Guid seriesId, string? seriesName, DateTimeOffset nowUtc);

    /// <summary>
    /// Unhides a series for a user. No-op when absent.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="seriesId">The series id.</param>
    /// <returns><see langword="true"/> when an entry was removed.</returns>
    bool Unhide(Guid userId, Guid seriesId);

    /// <summary>
    /// Removes every entry of a user. No-op when absent.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <returns><see langword="true"/> when anything was removed.</returns>
    bool ClearUser(Guid userId);
}
