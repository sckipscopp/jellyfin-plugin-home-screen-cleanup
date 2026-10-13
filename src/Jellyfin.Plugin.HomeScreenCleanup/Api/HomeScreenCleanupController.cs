using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HomeScreenCleanup.Api.Models;
using Jellyfin.Plugin.HomeScreenCleanup.Services;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using JellyfinUser = Jellyfin.Database.Implementations.Entities.User;

namespace Jellyfin.Plugin.HomeScreenCleanup.Api;

/// <summary>
/// User endpoints. Each acts on the caller's own user, or on <c>?userId=</c> when the caller is elevated.
/// </summary>
[ApiController]
[Route("HomeScreenCleanup")]
[Authorize]
public sealed class HomeScreenCleanupController : ControllerBase
{
    private const string ReasonUser = "user";

    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IHiddenSeriesStore _store;
    private readonly ResumeResetService _resumeResetService;
    private readonly IAuthorizationService _authorizationService;
    private readonly ILogger<HomeScreenCleanupController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HomeScreenCleanupController"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="store">The hidden-series store.</param>
    /// <param name="resumeResetService">The resume reset service.</param>
    /// <param name="authorizationService">The authorization service.</param>
    /// <param name="logger">The logger.</param>
    public HomeScreenCleanupController(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IHiddenSeriesStore store,
        ResumeResetService resumeResetService,
        IAuthorizationService authorizationService,
        ILogger<HomeScreenCleanupController> logger)
    {
        ArgumentNullException.ThrowIfNull(userManager);
        ArgumentNullException.ThrowIfNull(libraryManager);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(resumeResetService);
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(logger);
        _userManager = userManager;
        _libraryManager = libraryManager;
        _store = store;
        _resumeResetService = resumeResetService;
        _authorizationService = authorizationService;
        _logger = logger;
    }

    /// <summary>
    /// Removes an item from Continue Watching by marking it and all its versions played (resume position reset).
    /// </summary>
    /// <param name="itemId">The item id (any version).</param>
    /// <param name="userId">Optional target user (elevated callers only).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>200 with the reset ids; 404 when the item is missing or not visible; 400 when it is not a video.</returns>
    [HttpPost("ContinueWatching/{itemId}/Remove")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ResetResultDto>> RemoveFromContinueWatching(
        [FromRoute] Guid itemId,
        [FromQuery] Guid? userId,
        CancellationToken cancellationToken)
    {
        var (user, failure) = await ResolveUserAsync(userId).ConfigureAwait(false);
        if (user is null)
        {
            return failure ?? NotFound();
        }

        var item = FindVisibleItem(itemId, user);
        if (item is null)
        {
            return NotFound();
        }

        if (item is not Video video)
        {
            return Problem(detail: "Only videos have a Continue Watching position.", statusCode: StatusCodes.Status400BadRequest);
        }

        var resetIds = _resumeResetService.ResetProgress(user, video, cancellationToken);
        return new ResetResultDto
        {
            UserId = user.Id,
            ItemId = itemId,
            ResetItemIds = resetIds,
            Changed = resetIds.Count > 0,
        };
    }

    /// <summary>
    /// Hides a series from Next Up. Accepts an episode id or a series id.
    /// </summary>
    /// <param name="itemId">An episode or series id.</param>
    /// <param name="userId">Optional target user (elevated callers only).</param>
    /// <returns>200 with the entry; 404 when missing or not visible; 400 when not an episode/series; 409 when the list is full.</returns>
    [HttpPost("NextUp/Hidden/{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HiddenSeriesDto>> HideFromNextUp([FromRoute] Guid itemId, [FromQuery] Guid? userId)
    {
        var (user, failure) = await ResolveUserAsync(userId).ConfigureAwait(false);
        if (user is null)
        {
            return failure ?? NotFound();
        }

        var item = FindVisibleItem(itemId, user);
        if (item is null)
        {
            return NotFound();
        }

        Guid seriesId;
        string? seriesName;
        switch (item)
        {
            case Series series:
                seriesId = series.Id;
                seriesName = series.Name;
                break;
            case Episode episode:
                seriesId = episode.SeriesId;
                seriesName = episode.SeriesName;
                break;
            default:
                return Problem(detail: "Only episodes and series can be hidden from Next Up.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (seriesId == Guid.Empty)
        {
            return Problem(detail: "The episode is not linked to a series.", statusCode: StatusCodes.Status400BadRequest);
        }

        var targetUserId = user.Id;
        var result = _store.Hide(targetUserId, seriesId, seriesName, DateTimeOffset.UtcNow);
        if (result.Outcome == HideOutcome.LimitReached || result.Entry is null)
        {
            return Problem(
                detail: "The hidden list is full (" + HiddenSeriesStore.MaxEntriesPerUser.ToString(CultureInfo.InvariantCulture) + " series).",
                statusCode: StatusCodes.Status409Conflict);
        }

        if (result.Outcome == HideOutcome.Added)
        {
            _logger.SeriesHidden(seriesId, targetUserId);
        }

        return ToDto(result.Entry, result.Outcome == HideOutcome.AlreadyHidden);
    }

    /// <summary>
    /// Unhides a series from Next Up. Idempotent.
    /// </summary>
    /// <param name="seriesId">The series id.</param>
    /// <param name="userId">Optional target user (elevated callers only).</param>
    /// <returns>204.</returns>
    [HttpDelete("NextUp/Hidden/{seriesId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> UnhideFromNextUp([FromRoute] Guid seriesId, [FromQuery] Guid? userId)
    {
        var (user, failure) = await ResolveUserAsync(userId).ConfigureAwait(false);
        if (user is null)
        {
            return failure ?? NotFound();
        }

        var targetUserId = user.Id;
        if (_store.Unhide(targetUserId, seriesId))
        {
            _logger.SeriesUnhidden(seriesId, targetUserId, ReasonUser);
        }

        return NoContent();
    }

    /// <summary>
    /// Lists the series the user hid from Next Up, oldest first.
    /// </summary>
    /// <param name="userId">Optional target user (elevated callers only).</param>
    /// <returns>200 with the entries.</returns>
    [HttpGet("NextUp/Hidden")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<HiddenSeriesDto>>> ListHidden([FromQuery] Guid? userId)
    {
        var (user, failure) = await ResolveUserAsync(userId).ConfigureAwait(false);
        if (user is null)
        {
            return failure ?? NotFound();
        }

        var entries = _store.GetUserEntries(user.Id).Values
            .OrderBy(entry => entry.HiddenAtUtc)
            .ThenBy(entry => entry.SeriesId)
            .Select(entry => ToDto(entry, false))
            .ToArray();
        return Ok(entries);
    }

    /// <summary>
    /// Maps a store entry to its DTO.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="alreadyHidden">Whether the entry pre-existed.</param>
    /// <returns>The DTO.</returns>
    internal static HiddenSeriesDto ToDto(HiddenSeriesEntry entry, bool alreadyHidden)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new HiddenSeriesDto
        {
            SeriesId = entry.SeriesId,
            SeriesName = entry.SeriesName,
            HiddenAtUtc = entry.HiddenAtUtc,
            AlreadyHidden = alreadyHidden,
        };
    }

    private BaseItem? FindVisibleItem(Guid itemId, JellyfinUser user)
    {
        if (itemId == Guid.Empty)
        {
            return null;
        }

        var item = _libraryManager.GetItemById(itemId);
        return item is not null && item.IsVisibleStandalone(user) ? item : null;
    }

    private async Task<(JellyfinUser? User, ActionResult? Failure)> ResolveUserAsync(Guid? requestedUserId)
    {
        var principal = User;
        var isElevated = false;
        if (requestedUserId is { } requested && requested != Guid.Empty)
        {
            var authorization = await _authorizationService
                .AuthorizeAsync(principal, Policies.RequiresElevation)
                .ConfigureAwait(false);
            isElevated = authorization.Succeeded;
        }

        var resolution = CallerResolver.Resolve(
            principal.FindFirst(PluginConstants.UserIdClaimType)?.Value,
            principal.FindFirst(PluginConstants.IsApiKeyClaimType)?.Value,
            requestedUserId,
            isElevated);

        if (!resolution.IsSuccess)
        {
            return (null, Problem(detail: resolution.Error, statusCode: resolution.ErrorStatus));
        }

        var user = _userManager.GetUserById(resolution.UserId);
        return user is null ? (null, NotFound()) : (user, null);
    }
}
