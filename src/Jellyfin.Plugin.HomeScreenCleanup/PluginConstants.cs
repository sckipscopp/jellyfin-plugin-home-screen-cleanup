using System;
using System.Globalization;
using System.Reflection;

namespace Jellyfin.Plugin.HomeScreenCleanup;

/// <summary>
/// Fixed identifiers of the plugin. Kept free of Jellyfin.Controller types so tests can load it.
/// </summary>
internal static class PluginConstants
{
    /// <summary>
    /// The plugin GUID as a string; must match <c>guid</c> in <c>build.yaml</c>.
    /// </summary>
    public const string IdString = "48774b8b-b1b2-49b0-b3f4-4ebaf867a696";

    /// <summary>
    /// The plugin display name.
    /// </summary>
    public const string Name = "Home Screen Cleanup";

    /// <summary>
    /// The unversioned plugin data folder name (what <c>BasePlugin.DataFolderPath</c> resolves to).
    /// </summary>
    public const string DataFolderName = "Jellyfin.Plugin.HomeScreenCleanup";

    /// <summary>
    /// Manifest resource name of the configuration page.
    /// </summary>
    public const string ConfigPageResourceName = "Jellyfin.Plugin.HomeScreenCleanup.Configuration.configPage.html";

    /// <summary>
    /// Manifest resource name of the injected client script.
    /// </summary>
    public const string ClientScriptResourceName = "Jellyfin.Plugin.HomeScreenCleanup.Web.client.js";

    /// <summary>
    /// Name of the user id claim Jellyfin issues.
    /// </summary>
    public const string UserIdClaimType = "Jellyfin-UserId";

    /// <summary>
    /// Name of the claim Jellyfin sets for API-key authentication.
    /// </summary>
    public const string IsApiKeyClaimType = "Jellyfin-IsApiKey";

    /// <summary>
    /// The plugin GUID.
    /// </summary>
    public static readonly Guid Id = Guid.Parse(IdString, CultureInfo.InvariantCulture);

    /// <summary>
    /// The assembly version string, used for cache busting and the client script ETag.
    /// </summary>
    public static readonly string AssemblyVersion =
        typeof(PluginConstants).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

    /// <summary>
    /// Gets the plugin assembly.
    /// </summary>
    public static Assembly Assembly => typeof(PluginConstants).Assembly;
}
