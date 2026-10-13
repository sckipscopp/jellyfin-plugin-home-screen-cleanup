namespace Jellyfin.Plugin.HomeScreenCleanup.Web;

/// <summary>
/// Supplies the server's configured base path (test seam over the network configuration).
/// </summary>
public interface IBaseUrlProvider
{
    /// <summary>
    /// Gets the normalised base path: <c>""</c> or <c>"/jf"</c>-style (leading slash, no trailing slash).
    /// </summary>
    /// <returns>The base path.</returns>
    string ResolveBasePath();
}
