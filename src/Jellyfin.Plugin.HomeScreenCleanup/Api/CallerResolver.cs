using System;
using System.Globalization;

namespace Jellyfin.Plugin.HomeScreenCleanup.Api;

/// <summary>
/// Pure decision about which user a request acts on.
/// </summary>
internal static class CallerResolver
{
    /// <summary>
    /// HTTP 400.
    /// </summary>
    public const int BadRequest = 400;

    /// <summary>
    /// HTTP 403.
    /// </summary>
    public const int Forbidden = 403;

    /// <summary>
    /// Resolves the target user.
    /// </summary>
    /// <param name="userIdClaim">Value of the <c>Jellyfin-UserId</c> claim.</param>
    /// <param name="isApiKeyClaim">Value of the <c>Jellyfin-IsApiKey</c> claim.</param>
    /// <param name="requestedUserId">The optional <c>?userId=</c> query value.</param>
    /// <param name="isElevated">Whether the caller satisfies the RequiresElevation policy (only evaluated when a user id is requested).</param>
    /// <returns>The resolution.</returns>
    public static CallerResolution Resolve(string? userIdClaim, string? isApiKeyClaim, Guid? requestedUserId, bool isElevated)
    {
        var isApiKey = bool.TryParse(isApiKeyClaim, out var apiKeyFlag) && apiKeyFlag;
        var hasClaimUser = Guid.TryParse(userIdClaim, CultureInfo.InvariantCulture, out var claimUserId) && claimUserId != Guid.Empty;

        if (requestedUserId is { } requested && requested != Guid.Empty)
        {
            if (!isApiKey && hasClaimUser && requested == claimUserId)
            {
                return CallerResolution.Success(requested);
            }

            return isElevated
                ? CallerResolution.Success(requested)
                : CallerResolution.Failure(Forbidden, "Acting on another user requires administrator rights.");
        }

        if (isApiKey)
        {
            return CallerResolution.Failure(BadRequest, "API-key requests must pass userId.");
        }

        return hasClaimUser
            ? CallerResolution.Success(claimUserId)
            : CallerResolution.Failure(BadRequest, "The request is not bound to a user.");
    }
}
