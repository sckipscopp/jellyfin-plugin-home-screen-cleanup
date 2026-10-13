using System;

namespace Jellyfin.Plugin.HomeScreenCleanup.Web;

/// <summary>
/// Decides whether a request path is the web client's index page.
/// </summary>
internal static class IndexPathMatcher
{
    private static readonly string[] IndexSuffixes = ["/web", "/web/", "/web/index.html"];

    /// <summary>
    /// Normalises a configured base URL to <c>""</c> or <c>"/segment[/segment]"</c> (leading slash, no trailing slash).
    /// </summary>
    /// <param name="baseUrl">The configured base URL.</param>
    /// <returns>The normalised base path.</returns>
    public static string NormalizeBasePath(string? baseUrl)
    {
        var trimmed = (baseUrl ?? string.Empty).Trim().Trim('/');
        return trimmed.Length == 0 ? string.Empty : "/" + trimmed;
    }

    /// <summary>
    /// Returns whether <paramref name="fullPath"/> is <c>{base}/web</c>, <c>{base}/web/</c> or <c>{base}/web/index.html</c>.
    /// </summary>
    /// <param name="fullPath">PathBase + Path of the request.</param>
    /// <param name="basePath">The normalised base path, or <see langword="null"/> when unknown (segment-bounded suffix match).</param>
    /// <returns><see langword="true"/> on a match.</returns>
    public static bool IsIndexPath(string fullPath, string? basePath)
    {
        ArgumentNullException.ThrowIfNull(fullPath);

        foreach (var suffix in IndexSuffixes)
        {
            if (basePath is not null)
            {
                if (string.Equals(fullPath, basePath + suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (fullPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                // Suffix starts with '/', so the match is already segment-bounded.
                return true;
            }
        }

        return false;
    }
}
