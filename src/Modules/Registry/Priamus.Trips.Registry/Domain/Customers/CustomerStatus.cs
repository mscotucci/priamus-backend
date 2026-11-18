namespace Priamus.Trips.Registry.Domain.Customers;

/// <summary>
/// Whether a <see cref="Customer"/> can be chosen for new trips and contracts (PRD §4, RF-15).
/// </summary>
public enum CustomerStatus
{
    /// <summary>Attivo: can be chosen for new trips and contracts.</summary>
    Active,

    /// <summary>Disattivato: stays in the registry for the trips and contracts that name it.</summary>
    Deactivated,
}
