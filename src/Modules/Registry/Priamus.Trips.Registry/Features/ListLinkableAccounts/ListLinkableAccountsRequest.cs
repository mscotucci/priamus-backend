namespace Priamus.Trips.Registry.Features.ListLinkableAccounts;

/// <summary>
/// Query-string parameters for <c>GET /api/registry/drivers/linkable-accounts</c>.
/// </summary>
/// <remarks>
/// Mutable class (not record) required by ASP.NET Core query-string model binding.
/// Bound via <c>[AsParameters]</c> in the endpoint delegate signature.
/// </remarks>
public sealed class ListLinkableAccountsRequest
{
    /// <summary>
    /// The driver being edited: when it has an account, that account is offered too, even when
    /// disabled. When null, only the free enabled driver accounts are returned.
    /// </summary>
    public Guid? DriverId { get; set; }
}
