using System;
using System.Text;

namespace Jellyfin.Plugin.HomeScreenCleanup.Web;

/// <summary>
/// Pure, allocation-bounded rewrite of the web client's index.html.
/// </summary>
internal static class IndexHtmlRewriter
{
    /// <summary>
    /// Attribute that marks a page as already injected.
    /// </summary>
    public const string InjectedMarker = "data-hsc-injected";

    private const string BodyCloseTag = "</body>";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// Inserts <paramref name="tag"/> followed by a newline before the last <c>&lt;/body&gt;</c>.
    /// </summary>
    /// <param name="html">The original page bytes (UTF-8, optional BOM).</param>
    /// <param name="tag">The markup to insert.</param>
    /// <param name="result">The rewritten bytes (BOM preserved), or an empty array when <see langword="false"/>.</param>
    /// <returns><see langword="false"/> for invalid UTF-8, an already injected page or a page without <c>&lt;/body&gt;</c>.</returns>
    public static bool TryInject(ReadOnlySpan<byte> html, string tag, out byte[] result)
    {
        ArgumentNullException.ThrowIfNull(tag);
        result = [];

        var hasBom = html.StartsWith(Utf8Bom);
        var content = hasBom ? html[Utf8Bom.Length..] : html;

        string text;
        try
        {
            text = StrictUtf8.GetString(content);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        if (text.Contains(InjectedMarker, StringComparison.Ordinal))
        {
            return false;
        }

        var index = text.LastIndexOf(BodyCloseTag, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return false;
        }

        var rewritten = string.Concat(text.AsSpan(0, index), tag, "\n", text.AsSpan(index));
        var body = StrictUtf8.GetBytes(rewritten);
        if (!hasBom)
        {
            result = body;
            return true;
        }

        var withBom = new byte[Utf8Bom.Length + body.Length];
        Utf8Bom.CopyTo(withBom);
        body.CopyTo(withBom.AsSpan(Utf8Bom.Length));
        result = withBom;
        return true;
    }
}
