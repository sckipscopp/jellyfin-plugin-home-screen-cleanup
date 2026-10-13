using System;
using System.Globalization;
using Jellyfin.Plugin.HomeScreenCleanup.Api;
using Xunit;

namespace Jellyfin.Plugin.HomeScreenCleanup.Tests;

public sealed class CallerResolverTests
{
    private static readonly Guid Self = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    private static string SelfClaim => Self.ToString("N", CultureInfo.InvariantCulture);

    [Fact]
    public void User_without_userId_acts_on_self()
    {
        var result = CallerResolver.Resolve(SelfClaim, "False", null, isElevated: false);

        Assert.True(result.IsSuccess);
        Assert.Equal(Self, result.UserId);
    }

    [Fact]
    public void Empty_userId_query_is_treated_as_absent()
    {
        var result = CallerResolver.Resolve(SelfClaim, null, Guid.Empty, isElevated: false);

        Assert.True(result.IsSuccess);
        Assert.Equal(Self, result.UserId);
    }

    [Fact]
    public void User_naming_self_needs_no_elevation()
    {
        var result = CallerResolver.Resolve(Self.ToString("D", CultureInfo.InvariantCulture), "false", Self, isElevated: false);

        Assert.True(result.IsSuccess);
        Assert.Equal(Self, result.UserId);
    }

    [Fact]
    public void User_naming_other_user_without_elevation_is_forbidden()
    {
        var result = CallerResolver.Resolve(SelfClaim, "false", Other, isElevated: false);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.ErrorStatus);
        Assert.Equal(Guid.Empty, result.UserId);
    }

    [Fact]
    public void Admin_naming_other_user_acts_on_that_user()
    {
        var result = CallerResolver.Resolve(SelfClaim, "false", Other, isElevated: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(Other, result.UserId);
    }

    [Theory]
    [InlineData("True")]
    [InlineData("true")]
    public void Api_key_without_userId_is_bad_request(string isApiKey)
    {
        var result = CallerResolver.Resolve(Guid.Empty.ToString("N", CultureInfo.InvariantCulture), isApiKey, null, isElevated: true);

        Assert.Equal(400, result.ErrorStatus);
    }

    [Fact]
    public void Elevated_api_key_with_userId_acts_on_that_user()
    {
        var result = CallerResolver.Resolve(Guid.Empty.ToString("N", CultureInfo.InvariantCulture), "True", Other, isElevated: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(Other, result.UserId);
    }

    [Fact]
    public void Non_elevated_api_key_with_userId_is_forbidden()
    {
        var result = CallerResolver.Resolve(Guid.Empty.ToString("N", CultureInfo.InvariantCulture), "True", Other, isElevated: false);

        Assert.Equal(403, result.ErrorStatus);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000000000000000000000000000")]
    public void Missing_or_empty_claim_is_bad_request(string? claim)
    {
        var result = CallerResolver.Resolve(claim, null, null, isElevated: false);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.ErrorStatus);
        Assert.NotNull(result.Error);
    }
}
