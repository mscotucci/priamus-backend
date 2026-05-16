using Priamus.Trips.Identity.Domain.Users;

namespace Priamus.Trips.Identity.Features.ListUsers;

/// <summary>
/// A single item in the paginated list of users.
/// </summary>
/// <remarks>
/// Intentionally excludes <c>PasswordHash</c>, <c>CreatedBy</c>, and <c>LastModifiedBy</c>
/// to avoid leaking sensitive or internal actor identifiers over the wire.
/// </remarks>
public sealed record UserListItemDto(
    Guid Id,
    string Username,
    UserRole Role,
    UserStatus Status,
    DateTime CreatedAt,
    DateTime LastModifiedAt);
