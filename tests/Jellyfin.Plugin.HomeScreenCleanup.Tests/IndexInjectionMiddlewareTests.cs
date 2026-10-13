using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Plugin.HomeScreenCleanup.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace Jellyfin.Plugin.HomeScreenCleanup.Tests;

public sealed class IndexInjectionMiddlewareTests
{
    private const string Page = "<!DOCTYPE html><html><head></head><body dir=\"ltr\"><div id=\"reactRoot\"></div></body></html>";

    private readonly FakeSettings _settings = new();
    private readonly FakeBaseUrlProvider _baseUrl = new();

    [Theory]
    [InlineData("/web")]
    [InlineData("/web/")]
    [InlineData("/web/index.html")]
    [InlineData("/WEB/Index.html")]
    public async Task Injects_on_index_paths_and_fixes_headers(string path)
    {
        var (context, body) = await RunAsync(Serve(Page), path);

        var text = Encoding.UTF8.GetString(body.ToArray());
        Assert.Equal(1, CountOccurrences(text, IndexHtmlRewriter.InjectedMarker));
        Assert.Contains(IndexInjectionMiddleware.ScriptTag + "\n</body>", text, StringComparison.Ordinal);

        var headers = context.Response.Headers;
        Assert.False(headers.ContainsKey(HeaderNames.ETag));
        Assert.False(headers.ContainsKey(HeaderNames.LastModified));
        Assert.False(headers.ContainsKey(HeaderNames.AcceptRanges));
        Assert.Equal("no-cache", headers.CacheControl.ToString());
        Assert.Equal(body.Length, context.Response.ContentLength);
    }

    [Fact]
    public async Task Strips_conditional_range_and_encoding_request_headers_on_match()
    {
        HeaderDictionary? seen = null;
        RequestDelegate downstream = async ctx =>
        {
            seen = new HeaderDictionary();
            foreach (var header in ctx.Request.Headers)
            {
                seen[header.Key] = header.Value;
            }

            await Serve(Page)(ctx);
        };

        await RunAsync(downstream, "/web/", configure: AddConditionalHeaders);

        Assert.NotNull(seen);
        Assert.False(seen.ContainsKey(HeaderNames.AcceptEncoding));
        Assert.False(seen.ContainsKey(HeaderNames.Range));
        Assert.False(seen.ContainsKey(HeaderNames.IfRange));
        Assert.False(seen.ContainsKey(HeaderNames.IfNoneMatch));
        Assert.False(seen.ContainsKey(HeaderNames.IfModifiedSince));
        Assert.True(seen.ContainsKey(HeaderNames.UserAgent));
    }

    [Fact]
    public async Task Leaves_request_headers_alone_on_other_paths()
    {
        string? acceptEncoding = null;
        RequestDelegate downstream = ctx =>
        {
            acceptEncoding = ctx.Request.Headers.AcceptEncoding.ToString();
            return Task.CompletedTask;
        };

        await RunAsync(downstream, "/web/main.bundle.js", configure: AddConditionalHeaders);

        Assert.Equal("gzip, br", acceptEncoding);
    }

    [Fact]
    public async Task Captures_SendFileAsync()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "index.html");
        await File.WriteAllTextAsync(file, Page, new UTF8Encoding(false), TestContext.Current.CancellationToken);

        RequestDelegate downstream = async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/html";
            ctx.Response.ContentLength = new FileInfo(file).Length;
            await ctx.Response.SendFileAsync(file);
        };

        var (_, body) = await RunAsync(downstream, "/web/index.html");

        Assert.Contains(IndexInjectionMiddleware.ScriptTag, Encoding.UTF8.GetString(body.ToArray()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Captures_writes_through_the_pipe_writer()
    {
        RequestDelegate downstream = async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.WriteAsync(Page);
        };

        var (_, body) = await RunAsync(downstream, "/web/");

        Assert.Contains(IndexInjectionMiddleware.ScriptTag, Encoding.UTF8.GetString(body.ToArray()), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(304, "text/html")]
    [InlineData(206, "text/html")]
    [InlineData(404, "text/html")]
    [InlineData(200, "application/json")]
    public async Task Passes_through_non_rewritable_responses(int status, string contentType)
    {
        var content = status == 304 ? string.Empty : Page;
        var (context, body) = await RunAsync(Serve(content, status, contentType), "/web/index.html");

        Assert.Equal(content, Encoding.UTF8.GetString(body.ToArray()));
        Assert.Equal(status, context.Response.StatusCode);
        Assert.Equal("\"orig\"", context.Response.Headers.ETag.ToString());
    }

    [Fact]
    public async Task Passes_through_encoded_responses()
    {
        var (context, body) = await RunAsync(
            Serve(Page, configure: r => r.Headers.ContentEncoding = "gzip"),
            "/web/index.html");

        Assert.Equal(Page, Encoding.UTF8.GetString(body.ToArray()));
        Assert.Equal("\"orig\"", context.Response.Headers.ETag.ToString());
    }

    [Fact]
    public async Task Streams_through_unchanged_when_over_the_cap()
    {
        var large = "<html><body>" + new string('x', IndexInjectionMiddleware.MaxBufferBytes) + "</body></html>";
        RequestDelegate downstream = async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/html";
            var bytes = Encoding.UTF8.GetBytes(large);
            for (var offset = 0; offset < bytes.Length; offset += 81920)
            {
                await ctx.Response.Body.WriteAsync(bytes.AsMemory(offset, Math.Min(81920, bytes.Length - offset)));
            }
        };

        var (_, body) = await RunAsync(downstream, "/web/index.html");

        Assert.Equal(Encoding.UTF8.GetByteCount(large), body.Length);
        Assert.DoesNotContain(IndexHtmlRewriter.InjectedMarker, Encoding.UTF8.GetString(body.ToArray()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_nothing_when_injection_is_disabled()
    {
        _settings.Current.EnableScriptInjection = false;
        string? acceptEncoding = null;
        RequestDelegate downstream = async ctx =>
        {
            acceptEncoding = ctx.Request.Headers.AcceptEncoding.ToString();
            await Serve(Page)(ctx);
        };

        var (_, body) = await RunAsync(downstream, "/web/", configure: AddConditionalHeaders);

        Assert.Equal(Page, Encoding.UTF8.GetString(body.ToArray()));
        Assert.Equal("gzip, br", acceptEncoding);
    }

    [Fact]
    public async Task Ignores_non_get_requests()
    {
        var (_, body) = await RunAsync(Serve(Page), "/web/", method: "HEAD");

        Assert.Equal(Page, Encoding.UTF8.GetString(body.ToArray()));
    }

    [Fact]
    public async Task Honours_base_url()
    {
        _baseUrl.BasePath = "/jf";

        var (_, injected) = await RunAsync(Serve(Page), "/jf/web/");
        var (_, plain) = await RunAsync(Serve(Page), "/web/");
        var (_, withPathBase) = await RunAsync(Serve(Page), "/web/index.html", pathBase: "/jf");

        Assert.Contains(IndexHtmlRewriter.InjectedMarker, Encoding.UTF8.GetString(injected.ToArray()), StringComparison.Ordinal);
        Assert.Equal(Page, Encoding.UTF8.GetString(plain.ToArray()));
        Assert.Contains(IndexHtmlRewriter.InjectedMarker, Encoding.UTF8.GetString(withPathBase.ToArray()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Falls_back_to_suffix_match_when_base_url_is_unreadable()
    {
        _baseUrl.Throws = true;

        var (_, body) = await RunAsync(Serve(Page), "/jf/web/");

        Assert.Contains(IndexHtmlRewriter.InjectedMarker, Encoding.UTF8.GetString(body.ToArray()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Serves_original_bytes_when_the_rewriter_throws()
    {
        var (context, body) = await RunAsync(
            Serve(Page),
            "/web/",
            rewrite: _ => throw new InvalidOperationException("boom"));

        Assert.Equal(Page, Encoding.UTF8.GetString(body.ToArray()));
        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal("\"orig\"", context.Response.Headers.ETag.ToString());
    }

    [Fact]
    public async Task Propagates_downstream_exceptions_and_restores_the_body_feature()
    {
        var context = new DefaultHttpContext();
        var original = new StreamResponseBodyFeature(new MemoryStream());
        context.Features.Set<IHttpResponseBodyFeature>(original);
        context.Request.Method = "GET";
        context.Request.Path = "/web/";
        var middleware = new IndexInjectionMiddleware(
            _ => throw new InvalidOperationException("downstream"),
            _settings,
            _baseUrl,
            NullLogger<IndexInjectionMiddleware>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        Assert.Equal("downstream", ex.Message);
        Assert.Same(original, context.Features.Get<IHttpResponseBodyFeature>());
    }

    private static void AddConditionalHeaders(HttpRequest request)
    {
        request.Headers.AcceptEncoding = "gzip, br";
        request.Headers.Range = "bytes=0-10";
        request.Headers.IfRange = "\"orig\"";
        request.Headers.IfNoneMatch = "\"orig\"";
        request.Headers.IfModifiedSince = "Mon, 12 Oct 2026 00:00:00 GMT";
        request.Headers.UserAgent = "tests";
    }

    private static RequestDelegate Serve(string html, int status = 200, string contentType = "text/html", Action<HttpResponse>? configure = null)
    {
        return async ctx =>
        {
            var response = ctx.Response;
            response.StatusCode = status;
            response.ContentType = contentType;
            response.Headers.ETag = "\"orig\"";
            response.Headers.LastModified = "Mon, 12 Oct 2026 00:00:00 GMT";
            response.Headers.AcceptRanges = "bytes";
            configure?.Invoke(response);

            var bytes = Encoding.UTF8.GetBytes(html);
            response.ContentLength = bytes.Length;
            if (bytes.Length > 0)
            {
                await response.Body.WriteAsync(bytes);
            }
        };
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private async Task<(DefaultHttpContext Context, MemoryStream Body)> RunAsync(
        RequestDelegate downstream,
        string path,
        string method = "GET",
        string pathBase = "",
        Action<HttpRequest>? configure = null,
        Func<ReadOnlyMemory<byte>, byte[]?>? rewrite = null)
    {
        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(body));
        context.Request.Method = method;
        context.Request.PathBase = pathBase;
        context.Request.Path = path;
        configure?.Invoke(context.Request);

        var logger = NullLogger<IndexInjectionMiddleware>.Instance;
        var middleware = rewrite is null
            ? new IndexInjectionMiddleware(downstream, _settings, _baseUrl, logger)
            : new IndexInjectionMiddleware(downstream, _settings, _baseUrl, logger, rewrite);

        await middleware.InvokeAsync(context);
        return (context, body);
    }
}
