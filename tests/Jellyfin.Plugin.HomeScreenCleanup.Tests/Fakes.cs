using System;
using System.Globalization;
using System.IO;
using Jellyfin.Plugin.HomeScreenCleanup.Configuration;
using Jellyfin.Plugin.HomeScreenCleanup.Web;

namespace Jellyfin.Plugin.HomeScreenCleanup.Tests;

internal sealed class FakeSettings : ISettingsAccessor
{
    public PluginConfiguration Current { get; set; } = new();
}

internal sealed class FakeBaseUrlProvider : IBaseUrlProvider
{
    public string BasePath { get; set; } = string.Empty;

    public bool Throws { get; set; }

    public string ResolveBasePath()
    {
        if (Throws)
        {
            throw new InvalidOperationException("network configuration unavailable");
        }

        return BasePath;
    }
}

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hsc-tests-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}
