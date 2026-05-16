using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.SharedKernel.Paging;

namespace Priamus.Trips.Identity.Features.ListUsers;

/// <summary>
/// Query-string parameters for <c>GET /api/users</c>.
/// </summary>
/// <remarks>
/// Mutable class (not record) required by ASP.NET Core query-string model binding.
/// Bound via <c>[AsParameters]</c> in the endpoint delegate signature.
/// </remarks>
public sealed class ListUsersRequest : PagedRequest
{
    /// <summary>Filter by role. When null, all roles are included.</summary>
    public UserRole? Role { get; set; }

    /// <summary>Filter by status. When null, all statuses are included.</summary>
    public UserStatus? Status { get; set; }

    /// <summary>
    /// Case-insensitive substring match on <see cref="Domain.Users.ValueObjects.Username.Value"/>.
    /// When null or whitespace, no username filter is applied.
    /// </summary>
    public string? UsernameContains { get; set; }
}
