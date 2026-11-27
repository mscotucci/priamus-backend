using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Priamus.Trips.Identity.Infrastructure;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Identity.UnitTests.Infrastructure;

/// <summary>
/// Unit tests for how <see cref="HttpContextCurrentUser"/> reads the <c>driver_id</c> claim of the session.
/// </summary>
public sealed class HttpContextCurrentUserTests
{
    private static HttpContextCurrentUser UserWithClaims(params Claim[] claims)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test", nameType: "sub", roleType: "role")),
        };
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(context);
        return new HttpContextCurrentUser(accessor);
    }

    [Fact]
    public void DriverId_SessionWithDriverIdClaim_ReturnsTheClaimValue()
    {
        // Arrange
        var driverId = Guid.NewGuid();
        var user = UserWithClaims(new Claim("sub", Guid.NewGuid().ToString()), new Claim("driver_id", driverId.ToString()));

        // Act
        var result = user.DriverId;

        // Assert
        result.ShouldBe(driverId);
    }

    [Fact]
    public void DriverId_SessionWithoutDriverIdClaim_IsNull()
    {
        // Arrange
        var user = UserWithClaims(new Claim("sub", Guid.NewGuid().ToString()), new Claim("role", "Driver"));

        // Act
        var result = user.DriverId;

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void DriverId_NoHttpContext_IsNull()
    {
        // Arrange
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        var user = new HttpContextCurrentUser(accessor);

        // Act
        var result = user.DriverId;

        // Assert
        result.ShouldBeNull();
    }
}
