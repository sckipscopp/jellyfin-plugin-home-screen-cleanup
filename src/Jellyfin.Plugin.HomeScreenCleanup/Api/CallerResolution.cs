using System;

namespace Jellyfin.Plugin.HomeScreenCleanup.Api;

/// <summary>
/// Result of <see cref="CallerResolver.Resolve"/>: the user to act on, or an HTTP error.
/// </summary>
public sealed class CallerResolution
{
    private CallerResolution(Guid userId, int? errorStatus, string? error)
    {
        UserId = userId;
        ErrorStatus = errorStatus;
        Error = error;
    }

    /// <summary>
    /// Gets the user to act on (<see cref="Guid.Empty"/> on failure).
    /// </summary>
    public Guid UserId { get; }

    /// <summary>
    /// Gets the HTTP status to return on failure, or <see langword="null"/> on success.
    /// </summary>
    public int? ErrorStatus { get; }

    /// <summary>
    /// Gets a short error description on failure.
    /// </summary>
    public string? Error { get; }

    /// <summary>
    /// Gets a value indicating whether a user was resolved.
    /// </summary>
    public bool IsSuccess => ErrorStatus is null;

    /// <summary>
    /// Creates a success result.
    /// </summary>
    /// <param name="userId">The resolved user id.</param>
    /// <returns>The result.</returns>
    public static CallerResolution Success(Guid userId) => new(userId, null, null);

    /// <summary>
    /// Creates a failure result.
    /// </summary>
    /// <param name="status">The HTTP status.</param>
    /// <param name="error">The error description.</param>
    /// <returns>The result.</returns>
    public static CallerResolution Failure(int status, string error) => new(Guid.Empty, status, error);
}
