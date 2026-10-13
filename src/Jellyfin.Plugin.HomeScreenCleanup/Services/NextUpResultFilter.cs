using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading.Tasks;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HomeScreenCleanup.Services;

/// <summary>
/// Global MVC action filter that removes hidden series from <c>GET /Shows/NextUp</c> for every client.
/// Fail-open: any error leaves the server's own result in place; <c>next()</c> is called exactly once.
/// </summary>
public sealed class NextUpResultFilter : IAsyncActionFilter
{
    private const string JellyfinApiAssemblyName = "Jellyfin.Api";
    private const string TargetController = "TvShows";
    private const string TargetAction = "GetNextUp";

    private readonly IServiceProvider _services;
    private readonly ILogger<NextUpResultFilter> _logger;
    private IHiddenSeriesStore? _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="NextUpResultFilter"/> class.
    /// </summary>
    /// <param name="services">The root service provider; the store is resolved lazily so a store fault can never break every API request through this global filter.</param>
    /// <param name="logger">The logger.</param>
    public NextUpResultFilter(IServiceProvider services, ILogger<NextUpResultFilter> logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(logger);
        _services = services;
        _logger = logger;
    }

    private IHiddenSeriesStore Store => _store ??= _services.GetRequiredService<IHiddenSeriesStore>();

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Fail-open filter: any error must leave the upstream result untouched.")]
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        FilterPlan? plan = null;
        try
        {
            plan = Prepare(context);
        }
        catch (Exception ex)
        {
            _logger.NextUpFilterFailed(ex);
        }

        var executed = await next().ConfigureAwait(false);
        if (plan is null)
        {
            return;
        }

        try
        {
            if (executed.Exception is null
                && executed.Result is ObjectResult { Value: QueryResult<BaseItemDto> result } objectResult)
            {
                var hidden = plan.Hidden;
                var page = NextUpTrimmer.Trim(
                    result.Items,
                    hidden.ContainsKey,
                    plan.OriginalStartIndex,
                    plan.OriginalLimit,
                    plan.EnableTotalRecordCount,
                    result.TotalRecordCount);
                objectResult.Value = new QueryResult<BaseItemDto>(plan.OriginalStartIndex, page.TotalRecordCount, page.Items);
            }
        }
        catch (Exception ex)
        {
            _logger.NextUpFilterFailed(ex);
        }
    }

    private static bool IsNextUpAction(ActionExecutingContext context)
    {
        return context.ActionDescriptor is ControllerActionDescriptor descriptor
            && string.Equals(descriptor.ControllerName, TargetController, StringComparison.Ordinal)
            && string.Equals(descriptor.ActionName, TargetAction, StringComparison.Ordinal)
            && string.Equals(descriptor.ControllerTypeInfo.Assembly.GetName().Name, JellyfinApiAssemblyName, StringComparison.Ordinal);
    }

    private static Guid ResolveUserId(ActionExecutingContext context)
    {
        if (context.ActionArguments.TryGetValue("userId", out var argument) && argument is Guid requested && requested != Guid.Empty)
        {
            return requested;
        }

        var claim = context.HttpContext.User.FindFirst(PluginConstants.UserIdClaimType)?.Value;
        return Guid.TryParse(claim, CultureInfo.InvariantCulture, out var fromClaim) ? fromClaim : Guid.Empty;
    }

    private static int? ReadInt(IDictionary<string, object?> arguments, string name)
    {
        return arguments.TryGetValue(name, out var value) && value is int number ? number : null;
    }

    private FilterPlan? Prepare(ActionExecutingContext context)
    {
        if (!IsNextUpAction(context))
        {
            return null;
        }

        var arguments = context.ActionArguments;
        if (arguments.TryGetValue("seriesId", out var seriesArgument) && seriesArgument is Guid seriesId && seriesId != Guid.Empty)
        {
            return null;
        }

        var userId = ResolveUserId(context);
        if (userId == Guid.Empty)
        {
            return null;
        }

        var hidden = Store.GetUserEntries(userId);
        if (hidden.Count == 0)
        {
            return null;
        }

        var originalStart = ReadInt(arguments, "startIndex");
        var originalLimit = ReadInt(arguments, "limit");
        var enableTotal = !arguments.TryGetValue("enableTotalRecordCount", out var totalArgument) || totalArgument is not bool flag || flag;

        var window = NextUpTrimmer.ComputeWindow(originalStart, originalLimit, hidden.Count);
        arguments["startIndex"] = window.StartIndex;
        if (window.Limit is { } windowLimit)
        {
            arguments["limit"] = windowLimit;
        }

        return new FilterPlan(hidden, originalStart, originalLimit, enableTotal);
    }

    /// <summary>
    /// What the filter needs after the action ran.
    /// </summary>
    private sealed class FilterPlan
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FilterPlan"/> class.
        /// </summary>
        /// <param name="hidden">The user's hidden series.</param>
        /// <param name="originalStartIndex">The client's start index.</param>
        /// <param name="originalLimit">The client's limit.</param>
        /// <param name="enableTotalRecordCount">Whether the client asked for a total.</param>
        public FilterPlan(IReadOnlyDictionary<Guid, HiddenSeriesEntry> hidden, int? originalStartIndex, int? originalLimit, bool enableTotalRecordCount)
        {
            Hidden = hidden;
            OriginalStartIndex = originalStartIndex;
            OriginalLimit = originalLimit;
            EnableTotalRecordCount = enableTotalRecordCount;
        }

        /// <summary>
        /// Gets the user's hidden series.
        /// </summary>
        public IReadOnlyDictionary<Guid, HiddenSeriesEntry> Hidden { get; }

        /// <summary>
        /// Gets the client's start index.
        /// </summary>
        public int? OriginalStartIndex { get; }

        /// <summary>
        /// Gets the client's limit.
        /// </summary>
        public int? OriginalLimit { get; }

        /// <summary>
        /// Gets a value indicating whether the client asked for a total record count.
        /// </summary>
        public bool EnableTotalRecordCount { get; }
    }
}
