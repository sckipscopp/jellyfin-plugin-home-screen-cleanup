using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HomeScreenCleanup.Services;

/// <summary>
/// JSON-file backed <see cref="IHiddenSeriesStore"/>. State is an immutable snapshot published through a
/// volatile field (lock-free reads); writers serialize on a <see cref="Lock"/>, write a temp file, flush
/// it to disk, atomically replace the store file and only then publish the new snapshot.
/// </summary>
public sealed class HiddenSeriesStore : IHiddenSeriesStore
{
    /// <summary>
    /// The store file name inside the plugin data folder.
    /// </summary>
    public const string FileName = "hidden-series.json";

    /// <summary>
    /// The maximum number of hidden series per user.
    /// </summary>
    public const int MaxEntriesPerUser = 1000;

    private static readonly ImmutableDictionary<Guid, ImmutableDictionary<Guid, HiddenSeriesEntry>> EmptyState =
        ImmutableDictionary<Guid, ImmutableDictionary<Guid, HiddenSeriesEntry>>.Empty;

    private static readonly ImmutableDictionary<Guid, HiddenSeriesEntry> EmptyUser =
        ImmutableDictionary<Guid, HiddenSeriesEntry>.Empty;

    private readonly Lock _writeLock = new();
    private readonly string _folder;
    private readonly string _path;
    private readonly string _tempPath;
    private readonly ILogger _logger;

    private volatile ImmutableDictionary<Guid, ImmutableDictionary<Guid, HiddenSeriesEntry>> _state;

    /// <summary>
    /// Initializes a new instance of the <see cref="HiddenSeriesStore"/> class and loads the store file.
    /// </summary>
    /// <param name="dataFolderPath">The plugin data folder; created on first write.</param>
    /// <param name="logger">The logger.</param>
    public HiddenSeriesStore(string dataFolderPath, ILogger<HiddenSeriesStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFolderPath);
        ArgumentNullException.ThrowIfNull(logger);

        _folder = dataFolderPath;
        _path = Path.Combine(dataFolderPath, FileName);
        _tempPath = _path + ".tmp";
        _logger = logger;
        _state = Load();
    }

    /// <summary>
    /// Gets the full path of the store file.
    /// </summary>
    public string FilePath => _path;

    /// <inheritdoc />
    public IReadOnlyDictionary<Guid, HiddenSeriesEntry> GetUserEntries(Guid userId)
    {
        return _state.TryGetValue(userId, out var entries) ? entries : EmptyUser;
    }

    /// <inheritdoc />
    public IReadOnlyList<HiddenSeriesEntry> ListAll()
    {
        return Flatten(_state);
    }

    /// <inheritdoc />
    public HideResult Hide(Guid userId, Guid seriesId, string? seriesName, DateTimeOffset nowUtc)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id must not be empty.", nameof(userId));
        }

        if (seriesId == Guid.Empty)
        {
            throw new ArgumentException("Series id must not be empty.", nameof(seriesId));
        }

        lock (_writeLock)
        {
            var current = _state;
            var userMap = current.TryGetValue(userId, out var existingMap) ? existingMap : EmptyUser;

            if (userMap.TryGetValue(seriesId, out var existing))
            {
                return new HideResult(HideOutcome.AlreadyHidden, existing);
            }

            if (userMap.Count >= MaxEntriesPerUser)
            {
                return new HideResult(HideOutcome.LimitReached, null);
            }

            var entry = new HiddenSeriesEntry
            {
                UserId = userId,
                SeriesId = seriesId,
                SeriesName = seriesName,
                HiddenAtUtc = nowUtc.ToUniversalTime(),
            };

            var next = current.SetItem(userId, userMap.Add(seriesId, entry));
            Persist(next);
            _state = next;
            return new HideResult(HideOutcome.Added, entry);
        }
    }

    /// <inheritdoc />
    public bool Unhide(Guid userId, Guid seriesId)
    {
        lock (_writeLock)
        {
            var current = _state;
            if (!current.TryGetValue(userId, out var userMap) || !userMap.ContainsKey(seriesId))
            {
                return false;
            }

            var updated = userMap.Remove(seriesId);
            var next = updated.IsEmpty ? current.Remove(userId) : current.SetItem(userId, updated);
            Persist(next);
            _state = next;
            return true;
        }
    }

    /// <inheritdoc />
    public bool ClearUser(Guid userId)
    {
        lock (_writeLock)
        {
            var current = _state;
            if (!current.ContainsKey(userId))
            {
                return false;
            }

            var next = current.Remove(userId);
            Persist(next);
            _state = next;
            return true;
        }
    }

    private static List<HiddenSeriesEntry> Flatten(
        ImmutableDictionary<Guid, ImmutableDictionary<Guid, HiddenSeriesEntry>> state)
    {
        return state.Values
            .SelectMany(user => user.Values)
            .OrderBy(entry => entry.UserId)
            .ThenBy(entry => entry.HiddenAtUtc)
            .ThenBy(entry => entry.SeriesId)
            .ToList();
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The store is resolved from a global MVC filter: any load fault must degrade to an empty store, never to a failing constructor.")]
    private ImmutableDictionary<Guid, ImmutableDictionary<Guid, HiddenSeriesEntry>> Load()
    {
        RemoveStaleTemp();

        if (!File.Exists(_path))
        {
            _logger.StoreLoaded(_path, 0, 0);
            return EmptyState;
        }

        HiddenSeriesDocument? document;
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            document = JsonSerializer.Deserialize(stream, HiddenSeriesJsonContext.Default.HiddenSeriesDocument);
        }
        catch (Exception ex)
        {
            MoveAside(ex);
            return EmptyState;
        }

        if (document is null || document.SchemaVersion != HiddenSeriesDocument.CurrentSchemaVersion)
        {
            MoveAside(new InvalidDataException("Empty document or unsupported schemaVersion."));
            return EmptyState;
        }

        var builder = ImmutableDictionary.CreateBuilder<Guid, ImmutableDictionary<Guid, HiddenSeriesEntry>>();
        var valid = (document.Entries ?? [])
            .Where(entry => entry is not null && entry.UserId != Guid.Empty && entry.SeriesId != Guid.Empty)
            .GroupBy(entry => entry.UserId);

        var entryCount = 0;
        foreach (var group in valid)
        {
            var userBuilder = ImmutableDictionary.CreateBuilder<Guid, HiddenSeriesEntry>();
            foreach (var entry in group)
            {
                if (!userBuilder.ContainsKey(entry.SeriesId))
                {
                    userBuilder.Add(entry.SeriesId, entry);
                    entryCount++;
                }
            }

            builder[group.Key] = userBuilder.ToImmutable();
        }

        var state = builder.ToImmutable();
        _logger.StoreLoaded(_path, state.Count, entryCount);
        return state;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Best-effort housekeeping; the store must still start.")]
    private void MoveAside(Exception reason)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
        var corruptPath = Path.Combine(_folder, string.Concat("hidden-series.corrupt-", stamp, ".json"));
        try
        {
            File.Move(_path, corruptPath);
            _logger.StoreCorrupt(reason, _path, corruptPath);
        }
        catch (Exception ex)
        {
            _logger.StoreCorruptMoveFailed(ex, _path);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Best-effort housekeeping; the store must still start.")]
    private void RemoveStaleTemp()
    {
        try
        {
            if (File.Exists(_tempPath))
            {
                File.Delete(_tempPath);
            }
        }
        catch (Exception ex)
        {
            _logger.StaleTempRemoveFailed(ex, _tempPath);
        }
    }

    private void Persist(ImmutableDictionary<Guid, ImmutableDictionary<Guid, HiddenSeriesEntry>> next)
    {
        var document = new HiddenSeriesDocument { Entries = Flatten(next) };

        Directory.CreateDirectory(_folder);
        using (var stream = new FileStream(_tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, document, HiddenSeriesJsonContext.Default.HiddenSeriesDocument);
            stream.Flush(flushToDisk: true);
        }

        File.Move(_tempPath, _path, overwrite: true);
    }
}
