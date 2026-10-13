using System;
using System.Linq;
using System.Text;
using Jellyfin.Plugin.HomeScreenCleanup.Web;
using Xunit;

namespace Jellyfin.Plugin.HomeScreenCleanup.Tests;

public sealed class IndexHtmlRewriterTests
{
    private const string Tag = "<script data-hsc-injected=\"1\"></script>";

    [Fact]
    public void Inserts_tag_and_newline_before_body_close()
    {
        var html = Encoding.UTF8.GetBytes("<html><body dir=\"ltr\"><div id=\"reactRoot\"></div></body></html>");

        Assert.True(IndexHtmlRewriter.TryInject(html, Tag, out var result));

        Assert.Equal(
            "<html><body dir=\"ltr\"><div id=\"reactRoot\"></div>" + Tag + "\n</body></html>",
            Encoding.UTF8.GetString(result));
    }

    [Fact]
    public void Uses_the_last_body_close_and_ignores_case()
    {
        var html = Encoding.UTF8.GetBytes("<body><script>var s = '</body>';</script></BODY>");

        Assert.True(IndexHtmlRewriter.TryInject(html, Tag, out var result));

        Assert.Equal("<body><script>var s = '</body>';</script>" + Tag + "\n</BODY>", Encoding.UTF8.GetString(result));
    }

    [Fact]
    public void Preserves_utf8_bom_and_non_ascii_text()
    {
        byte[] bom = [0xEF, 0xBB, 0xBF];
        var html = bom.Concat(Encoding.UTF8.GetBytes("<body>À suivre</body>")).ToArray();

        Assert.True(IndexHtmlRewriter.TryInject(html, Tag, out var result));

        Assert.Equal(bom, result.Take(3).ToArray());
        Assert.Equal("<body>À suivre" + Tag + "\n</body>", Encoding.UTF8.GetString(result.AsSpan(3)));
    }

    [Fact]
    public void Rejects_invalid_utf8()
    {
        byte[] html = [0x3C, 0x62, 0x6F, 0x64, 0x79, 0x3E, 0xC3, 0x28, 0x3C, 0x2F, 0x62, 0x6F, 0x64, 0x79, 0x3E];

        Assert.False(IndexHtmlRewriter.TryInject(html, Tag, out var result));
        Assert.Empty(result);
    }

    [Fact]
    public void Is_idempotent_on_marker()
    {
        var html = Encoding.UTF8.GetBytes("<body>" + Tag + "</body>");

        Assert.False(IndexHtmlRewriter.TryInject(html, Tag, out var result));
        Assert.Empty(result);
    }

    [Fact]
    public void Rejects_page_without_body_close()
    {
        var html = Encoding.UTF8.GetBytes("{\"not\":\"html\"}");

        Assert.False(IndexHtmlRewriter.TryInject(html, Tag, out _));
    }

    [Fact]
    public void Script_tag_constant_is_relative_versioned_and_marked()
    {
        var tag = IndexInjectionMiddleware.ScriptTag;

        Assert.StartsWith("<script defer src=\"../HomeScreenCleanup/client.js?v=", tag, StringComparison.Ordinal);
        Assert.Contains(PluginConstants.AssemblyVersion, tag, StringComparison.Ordinal);
        Assert.Contains(IndexHtmlRewriter.InjectedMarker, tag, StringComparison.Ordinal);
    }
}
