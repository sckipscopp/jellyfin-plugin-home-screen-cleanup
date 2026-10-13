using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.HomeScreenCleanup.Services;

/// <summary>
/// System.Text.Json source-generation context for the store file.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(HiddenSeriesDocument))]
internal sealed partial class HiddenSeriesJsonContext : JsonSerializerContext
{
}
