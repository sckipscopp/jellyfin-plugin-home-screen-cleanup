using System;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;

namespace Jellyfin.Plugin.HomeScreenCleanup.Web;

/// <summary>
/// Reads the base URL from the server network configuration. The only file that touches that API.
/// </summary>
public sealed class BaseUrlProvider : IBaseUrlProvider
{
    private readonly IServerConfigurationManager _configurationManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseUrlProvider"/> class.
    /// </summary>
    /// <param name="configurationManager">The server configuration manager.</param>
    public BaseUrlProvider(IServerConfigurationManager configurationManager)
    {
        ArgumentNullException.ThrowIfNull(configurationManager);
        _configurationManager = configurationManager;
    }

    /// <inheritdoc />
    public string ResolveBasePath()
    {
        // VERIFY-12.2: GetNetworkConfiguration() is an extension in MediaBrowser.Common.Net
        // (NetworkConfigurationExtensions) through 10.11; fix the using above if 12.2 moved it.
        return IndexPathMatcher.NormalizeBasePath(_configurationManager.GetNetworkConfiguration().BaseUrl);
    }
}
