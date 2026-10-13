using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Jellyfin.Plugin.HomeScreenCleanup.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.HomeScreenCleanup.Web;

/// <summary>
/// Outermost middleware that adds the client script tag to the web client's index.html in memory.
/// Fail-open: anything unexpected serves the original bytes; downstream exceptions always propagate.
/// </summary>
public sealed class IndexInjectionMiddleware
{
    /// <summary>
    /// Maximum number of response bytes buffered before the middleware gives up and streams through.
    /// </summary>
    public const int MaxBufferBytes = 4 * 1024 * 1024;

    /// <summary>
    /// The injected script tag. Relative to <c>{base}/web/</c>, cache-busted by the assembly version.
    /// </summary>
    internal static readonly string ScriptTag =
        "<script defer src=\"../HomeScreenCleanup/client.js?v=" + PluginConstants.AssemblyVersion + "\" data-hsc-injected=\"1\"></script>";

    private static readonly string[] StrippedRequestHeaders =
    [
        HeaderNames.AcceptEncoding,
        HeaderNames.Range,
        HeaderNames.IfRange,
        HeaderNames.IfNoneMatch,
        HeaderNames.IfModifiedSince,
    ];

    private readonly RequestDelegate _next;
    private readonly ISettingsAccessor _settings;
    private readonly IBaseUrlProvider _baseUrlProvider;
    private readonly ILogger<IndexInjectionMiddleware> _logger;
    private readonly Func<ReadOnlyMemory<byte>, byte[]?> _rewrite;

    /// <summary>
    /// Initializes a new instance of the <see cref="IndexInjectionMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware.</param>
    /// <param name="settings">The settings accessor.</param>
    /// <param name="baseUrlProvider">The base URL provider.</param>
    /// <param name="logger">The logger.</param>
    public IndexInjectionMiddleware(
        RequestDelegate next,
        ISettingsAccessor settings,
        IBaseUrlProvider baseUrlProvider,
        ILogger<IndexInjectionMiddleware> logger)
        : this(next, settings, baseUrlProvider, logger, DefaultRewrite)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IndexInjectionMiddleware"/> class with a custom rewriter (tests).
    /// </summary>
    /// <param name="next">The next middleware.</param>
    /// <param name="settings">The settings accessor.</param>
    /// <param name="baseUrlProvider">The base URL provider.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="rewrite">Returns the rewritten page, or <see langword="null"/> to keep the original.</param>
    internal IndexInjectionMiddleware(
        RequestDelegate next,
        ISettingsAccessor settings,
        IBaseUrlProvider baseUrlProvider,
        ILogger<IndexInjectionMiddleware> logger,
        Func<ReadOnlyMemory<byte>, byte[]?> rewrite)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(baseUrlProvider);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(rewrite);
        _next = next;
        _settings = settings;
        _baseUrlProvider = baseUrlProvider;
        _logger = logger;
        _rewrite = rewrite;
    }

    /// <summary>
    /// Processes a request.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task that completes when the response is written.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var originalBody = context.Features.Get<IHttpResponseBodyFeature>();
        if (originalBody is null || !ShouldHandle(context.Request))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var requestHeaders = context.Request.Headers;
        foreach (var header in StrippedRequestHeaders)
        {
            requestHeaders.Remove(header);
        }

        using var capture = new CappedBufferStream(originalBody.Stream, MaxBufferBytes);
        var captureFeature = new StreamResponseBodyFeature(capture, originalBody);
        context.Features.Set<IHttpResponseBodyFeature>(captureFeature);
        try
        {
            await _next(context).ConfigureAwait(false);

            // Flushes anything written through the PipeWriter (Response.WriteAsync / BodyWriter) into the capture.
            await captureFeature.CompleteAsync().ConfigureAwait(false);
        }
        finally
        {
            context.Features.Set(originalBody);
            captureFeature.Dispose();
        }

        if (capture.Overflowed)
        {
            // Already streamed through unchanged.
            return;
        }

        var output = capture.GetBufferedBytes();
        var response = context.Response;
        if (!response.HasStarted
            && response.StatusCode == StatusCodes.Status200OK
            && IsHtml(response.ContentType)
            && !response.Headers.ContainsKey(HeaderNames.ContentEncoding))
        {
            var rewritten = TryRewrite(output);
            if (rewritten is not null)
            {
                var headers = response.Headers;
                headers.Remove(HeaderNames.ETag);
                headers.Remove(HeaderNames.LastModified);
                headers.Remove(HeaderNames.AcceptRanges);
                headers.Remove(HeaderNames.ContentRange);
                headers.CacheControl = "no-cache";
                response.ContentLength = rewritten.Length;
                output = rewritten;
                _logger.IndexInjected(rewritten.Length);
            }
        }

        if (!output.IsEmpty)
        {
            await originalBody.Stream.WriteAsync(output, context.RequestAborted).ConfigureAwait(false);
        }
    }

    private static byte[]? DefaultRewrite(ReadOnlyMemory<byte> html)
    {
        return IndexHtmlRewriter.TryInject(html.Span, ScriptTag, out var result) ? result : null;
    }

    private static bool IsHtml(string? contentType)
    {
        return contentType is not null && contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Fail-open: a rewriter fault must serve the original page.")]
    private byte[]? TryRewrite(ReadOnlyMemory<byte> html)
    {
        try
        {
            return _rewrite(html);
        }
        catch (Exception ex)
        {
            _logger.InjectionFailed(ex);
            return null;
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Fail-open: an unreadable base URL falls back to suffix matching.")]
    private bool ShouldHandle(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method))
        {
            return false;
        }

        var fullPath = request.PathBase.Add(request.Path).Value ?? string.Empty;

        // Cheap suffix pre-check first so the base URL is only read for candidate paths.
        if (!IndexPathMatcher.IsIndexPath(fullPath, null) || !_settings.Current.EnableScriptInjection)
        {
            return false;
        }

        string? basePath;
        try
        {
            basePath = _baseUrlProvider.ResolveBasePath();
        }
        catch (Exception ex)
        {
            _logger.BaseUrlUnavailable(ex);
            basePath = null;
        }

        return IndexPathMatcher.IsIndexPath(fullPath, basePath);
    }
}
