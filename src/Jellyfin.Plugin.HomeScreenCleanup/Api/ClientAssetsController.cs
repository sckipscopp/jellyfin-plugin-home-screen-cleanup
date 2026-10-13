using System;
using System.IO;
using System.Threading;
using Jellyfin.Plugin.HomeScreenCleanup.Api.Models;
using Jellyfin.Plugin.HomeScreenCleanup.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.HomeScreenCleanup.Api;

/// <summary>
/// Serves the client script (anonymous, it holds no data) and the client feature toggles.
/// </summary>
[ApiController]
[Route("HomeScreenCleanup")]
public sealed class ClientAssetsController : ControllerBase
{
    private const string ScriptContentType = "application/javascript; charset=utf-8";
    private const string ImmutableCacheControl = "public, max-age=31536000, immutable";

    private static readonly Lazy<byte[]> ClientScript = new(LoadClientScript, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly ISettingsAccessor _settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientAssetsController"/> class.
    /// </summary>
    /// <param name="settings">The settings accessor.</param>
    public ClientAssetsController(ISettingsAccessor settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    /// <summary>
    /// Returns the client script. Immutable caching when <paramref name="v"/> matches the plugin version.
    /// </summary>
    /// <param name="v">The cache-busting version from the injected script tag.</param>
    /// <returns>200 with the script, or 304 for a matching <c>If-None-Match</c>.</returns>
    [HttpGet("client.js")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public ActionResult ClientScriptFile([FromQuery] string? v)
    {
        var headers = Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.CacheControl = string.Equals(v, PluginConstants.AssemblyVersion, StringComparison.Ordinal)
            ? ImmutableCacheControl
            : "no-cache";

        return new FileContentResult(ClientScript.Value, ScriptContentType)
        {
            EntityTag = new EntityTagHeaderValue("\"" + PluginConstants.AssemblyVersion + "\""),
        };
    }

    /// <summary>
    /// Returns which menu entries the client should offer.
    /// </summary>
    /// <returns>200 with the toggles.</returns>
    [HttpGet("ClientConfig")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<ClientConfigDto> ClientConfig()
    {
        Response.Headers.CacheControl = "no-store";
        var configuration = _settings.Current;
        return new ClientConfigDto
        {
            EnableContinueWatchingAction = configuration.EnableContinueWatchingAction,
            EnableNextUpAction = configuration.EnableNextUpAction,
        };
    }

    private static byte[] LoadClientScript()
    {
        using var resource = PluginConstants.Assembly.GetManifestResourceStream(PluginConstants.ClientScriptResourceName)
            ?? throw new InvalidOperationException("The embedded client script is missing.");
        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        return buffer.ToArray();
    }
}
