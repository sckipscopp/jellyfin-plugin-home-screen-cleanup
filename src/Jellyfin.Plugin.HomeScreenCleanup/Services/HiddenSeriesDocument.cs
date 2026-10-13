using System.Collections.Generic;

namespace Jellyfin.Plugin.HomeScreenCleanup.Services;

/// <summary>
/// On-disk shape of <c>hidden-series.json</c>.
/// </summary>
public sealed class HiddenSeriesDocument
{
    /// <summary>
    /// The only schema version this build reads and writes.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// Gets or sets the schema version.
    /// </summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>
    /// Gets or sets the entries.
    /// </summary>
    public IReadOnlyList<HiddenSeriesEntry> Entries { get; set; } = [];
}
