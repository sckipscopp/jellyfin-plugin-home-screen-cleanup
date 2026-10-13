using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.HomeScreenCleanup.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.HomeScreenCleanup.Tests;

public sealed class HiddenSeriesStoreTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _temp = new();

    private string StorePath => Path.Combine(_temp.Path, HiddenSeriesStore.FileName);

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Starts_empty_without_creating_files()
    {
        var folder = Path.Combine(_temp.Path, "not-created-yet");

        var store = new HiddenSeriesStore(folder, NullLogger<HiddenSeriesStore>.Instance);

        Assert.Empty(store.ListAll());
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Creates_the_folder_and_round_trips()
    {
        var folder = Path.Combine(_temp.Path, "plugins", "Jellyfin.Plugin.HomeScreenCleanup");
        var user = Guid.NewGuid();
        var seriesA = Guid.NewGuid();
        var seriesB = Guid.NewGuid();

        var store = new HiddenSeriesStore(folder, NullLogger<HiddenSeriesStore>.Instance);
        store.Hide(user, seriesA, "Show A", T0);
        store.Hide(user, seriesB, null, T0.AddMinutes(1));

        var reloaded = new HiddenSeriesStore(folder, NullLogger<HiddenSeriesStore>.Instance);
        var entries = reloaded.GetUserEntries(user);
        Assert.Equal(2, entries.Count);
        Assert.Equal("Show A", entries[seriesA].SeriesName);
        Assert.Null(entries[seriesB].SeriesName);
        Assert.Equal(T0, entries[seriesA].HiddenAtUtc);

        var json = File.ReadAllText(Path.Combine(folder, HiddenSeriesStore.FileName));
        Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);
        Assert.Contains("\"seriesId\"", json, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(folder, HiddenSeriesStore.FileName + ".tmp")));
    }

    [Fact]
    public void Hide_is_idempotent_and_keeps_the_first_timestamp()
    {
        var store = NewStore();
        var user = Guid.NewGuid();
        var series = Guid.NewGuid();

        var first = store.Hide(user, series, "Show", T0);
        var second = store.Hide(user, series, "Renamed", T0.AddDays(1));

        Assert.Equal(HideOutcome.Added, first.Outcome);
        Assert.Equal(HideOutcome.AlreadyHidden, second.Outcome);
        Assert.Equal(T0, second.Entry!.HiddenAtUtc);
        Assert.Equal("Show", second.Entry.SeriesName);
        Assert.Single(store.GetUserEntries(user));
    }

    [Fact]
    public void Users_are_isolated()
    {
        var store = NewStore();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var series = Guid.NewGuid();

        store.Hide(alice, series, "Show", T0);

        Assert.Single(store.GetUserEntries(alice));
        Assert.Empty(store.GetUserEntries(bob));
        Assert.False(store.Unhide(bob, series));
        Assert.Single(store.GetUserEntries(alice));
    }

    [Fact]
    public void Unhide_and_ClearUser_are_no_ops_when_absent_and_persist_when_present()
    {
        var store = NewStore();
        var user = Guid.NewGuid();
        var seriesA = Guid.NewGuid();
        var seriesB = Guid.NewGuid();

        Assert.False(store.Unhide(user, seriesA));
        Assert.False(store.ClearUser(user));

        store.Hide(user, seriesA, null, T0);
        store.Hide(user, seriesB, null, T0);
        Assert.True(store.Unhide(user, seriesA));
        Assert.Single(NewStore().GetUserEntries(user));

        Assert.True(store.ClearUser(user));
        Assert.Empty(NewStore().ListAll());
    }

    [Fact]
    public void Corrupt_file_is_moved_aside_and_store_starts_empty()
    {
        File.WriteAllText(StorePath, "{ this is not json");

        var store = NewStore();

        Assert.Empty(store.ListAll());
        Assert.False(File.Exists(StorePath));
        Assert.Single(Directory.GetFiles(_temp.Path, "hidden-series.corrupt-*.json"));

        store.Hide(Guid.NewGuid(), Guid.NewGuid(), null, T0);
        Assert.Single(NewStore().ListAll());
    }

    [Fact]
    public void Unknown_schema_version_is_moved_aside()
    {
        File.WriteAllText(StorePath, "{\"schemaVersion\": 99, \"entries\": []}");

        var store = NewStore();

        Assert.Empty(store.ListAll());
        Assert.Single(Directory.GetFiles(_temp.Path, "hidden-series.corrupt-*.json"));
    }

    [Fact]
    public void Stale_temp_file_is_ignored_and_removed()
    {
        var user = Guid.NewGuid();
        NewStore().Hide(user, Guid.NewGuid(), "Kept", T0);
        File.WriteAllText(StorePath + ".tmp", "garbage from a crashed write");

        var store = NewStore();

        Assert.Single(store.GetUserEntries(user));
        Assert.False(File.Exists(StorePath + ".tmp"));
    }

    [Fact]
    public void Failed_persist_leaves_memory_and_disk_unchanged()
    {
        var user = Guid.NewGuid();
        var store = NewStore();
        store.Hide(user, Guid.NewGuid(), "Existing", T0);

        // A directory where the temp file must go makes the write fail.
        Directory.CreateDirectory(StorePath + ".tmp");

        var ex = Record.Exception(() => store.Hide(user, Guid.NewGuid(), "New", T0));

        Assert.True(ex is IOException or UnauthorizedAccessException, ex?.GetType().FullName);
        Assert.Single(store.GetUserEntries(user));
        Directory.Delete(StorePath + ".tmp");
        Assert.Single(NewStore().GetUserEntries(user));
    }

    [Fact]
    public void Refuses_to_exceed_the_per_user_cap()
    {
        var user = Guid.NewGuid();
        var document = new HiddenSeriesDocument
        {
            Entries = Enumerable.Range(0, HiddenSeriesStore.MaxEntriesPerUser)
                .Select(i => new HiddenSeriesEntry { UserId = user, SeriesId = Guid.NewGuid(), HiddenAtUtc = T0.AddSeconds(i) })
                .ToArray(),
        };
        File.WriteAllText(StorePath, JsonSerializer.Serialize(document, HiddenSeriesJsonContext.Default.HiddenSeriesDocument));
        var store = NewStore();
        var existing = store.GetUserEntries(user).Keys.First();

        var refused = store.Hide(user, Guid.NewGuid(), null, T0);
        var again = store.Hide(user, existing, null, T0);
        var otherUser = store.Hide(Guid.NewGuid(), Guid.NewGuid(), null, T0);

        Assert.Equal(HideOutcome.LimitReached, refused.Outcome);
        Assert.Null(refused.Entry);
        Assert.Equal(HideOutcome.AlreadyHidden, again.Outcome);
        Assert.Equal(HideOutcome.Added, otherUser.Outcome);
        Assert.Equal(HiddenSeriesStore.MaxEntriesPerUser, store.GetUserEntries(user).Count);
    }

    [Fact]
    public async Task Concurrent_writers_lose_nothing()
    {
        var store = NewStore();
        var users = new[] { Guid.NewGuid(), Guid.NewGuid() };

        var tasks = Enumerable.Range(0, 64)
            .Select(i => Task.Run(() =>
            {
                var result = store.Hide(users[i % 2], Guid.NewGuid(), null, T0);
                _ = store.GetUserEntries(users[(i + 1) % 2]).Count;
                return result;
            }))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal(HideOutcome.Added, r.Outcome));
        Assert.Equal(32, store.GetUserEntries(users[0]).Count);
        Assert.Equal(32, store.GetUserEntries(users[1]).Count);
        Assert.Equal(64, NewStore().ListAll().Count);
    }

    [Fact]
    public void Rejects_empty_ids()
    {
        var store = NewStore();

        Assert.Throws<ArgumentException>(() => store.Hide(Guid.Empty, Guid.NewGuid(), null, T0));
        Assert.Throws<ArgumentException>(() => store.Hide(Guid.NewGuid(), Guid.Empty, null, T0));
    }

    private HiddenSeriesStore NewStore() => new(_temp.Path, NullLogger<HiddenSeriesStore>.Instance);
}
