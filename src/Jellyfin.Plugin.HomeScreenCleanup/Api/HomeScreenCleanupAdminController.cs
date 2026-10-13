using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.HomeScreenCleanup.Api.Models;
using Jellyfin.Plugin.HomeScreenCleanup.Services;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HomeScreenCleanup.Api;

/// <summary>
/// Administrator endpoints over every user's hidden list (used by the configuration page).
/// </summary>
[ApiController]
[Route("HomeScreenCleanup/Admin")]
[Authorize(Policy = Policies.RequiresElevation)]
public sealed class HomeScreenCleanupAdminController : ControllerBase
{
    private const string ReasonAdmin = "admin";

    private readonly IUserManager _userManager;
    private readonly IHiddenSeriesStore _store;
    private readonly ILogger<HomeScreenCleanupAdminController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HomeScreenCleanupAdminController"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="store">The hidden-series store.</param>
    /// <param name="logger">The logger.</param>
    public HomeScreenCleanupAdminController(
        IUserManager userManager,
        IHiddenSeriesStore store,
        ILogger<HomeScreenCleanupAdminController> logger)
    {
        ArgumentNullException.ThrowIfNull(userManager);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);
        _userManager = userManager;
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Lists every user's hidden series.
    /// </summary>
    /// <returns>200 with one list per user that has entries.</returns>
    [HttpGet("Hidden")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<UserHiddenListDto>> ListAllHidden()
    {
        var lists = _store.ListAll()
            .GroupBy(entry => entry.UserId)
            .Select(group => new UserHiddenListDto
            {
                UserId = group.Key,
                UserName = _userManager.GetUserById(group.Key)?.Username,
                Entries = group.Select(entry => HomeScreenCleanupController.ToDto(entry, false)).ToArray(),
            })
            .ToArray();
        return Ok(lists);
    }

    /// <summary>
    /// Unhides one series for one user. Idempotent.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="seriesId">The series id.</param>
    /// <returns>204.</returns>
    [HttpDelete("Hidden/{userId}/{seriesId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult UnhideForUser([FromRoute] Guid userId, [FromRoute] Guid seriesId)
    {
        if (_store.Unhide(userId, seriesId))
        {
            _logger.SeriesUnhidden(seriesId, userId, ReasonAdmin);
        }

        return NoContent();
    }

    /// <summary>
    /// Clears a user's hidden list. Idempotent.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <returns>204.</returns>
    [HttpDelete("Hidden/{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult ClearUser([FromRoute] Guid userId)
    {
        if (_store.ClearUser(userId))
        {
            _logger.UserCleared(userId);
        }

        return NoContent();
    }
}
