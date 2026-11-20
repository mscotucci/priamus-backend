using Priamus.Trips.Registry.Domain.Customers.Events;
using Priamus.Trips.Registry.Domain.Customers.ValueObjects;
using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Registry.Domain.Customers;

/// <summary>
/// A company or public body the company carries goods for and bills (PRD §4, RF-15). Identified
/// for people by its company name and, when it has one, by its <see cref="ValueObjects.VatNumber"/>;
/// has exactly one <see cref="CustomerStatus"/>.
/// </summary>
public sealed class Customer : AggregateRoot
{
    /// <summary>Inclusive maximum length of the company name once trimmed.</summary>
    public const int CompanyNameMaxLength = 150;

    /// <summary>Inclusive maximum length of the address once trimmed.</summary>
    public const int AddressMaxLength = 200;

    private Customer()
    {
        // EF only. Members are populated by EF Core via property setters/backing fields.
        CompanyName = null!;
    }

    private Customer(
        CustomerId id,
        string companyName,
        VatNumber? vatNumber,
        TaxCode? taxCode,
        string? address,
        DateTime createdAt,
        UserId createdBy)
    {
        Id = id;
        CompanyName = companyName;
        VatNumber = vatNumber;
        TaxCode = taxCode;
        Address = address;
        Status = CustomerStatus.Active;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        LastModifiedAt = createdAt;
        LastModifiedBy = createdBy;
    }

    /// <summary>Strongly-typed identifier.</summary>
    public CustomerId Id { get; private set; }

    /// <summary>Name as it appears on the customer's documents; trimmed, at most <see cref="CompanyNameMaxLength"/> characters. Not unique.</summary>
    public string CompanyName { get; private set; }

    /// <summary>VAT number in normal form; <c>null</c> for a customer without one. Unique among all customers.</summary>
    public VatNumber? VatNumber { get; private set; }

    /// <summary>Tax code in normal form; <c>null</c> when not entered. Not unique.</summary>
    public TaxCode? TaxCode { get; private set; }

    /// <summary>One line of free text, trimmed, at most <see cref="AddressMaxLength"/> characters; <c>null</c> when not entered.</summary>
    public string? Address { get; private set; }

    /// <summary>Whether the customer can be chosen for new trips and contracts.</summary>
    public CustomerStatus Status { get; private set; }

    /// <summary>UTC instant at which the customer was created.</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>Identifier of the actor that created this customer.</summary>
    public UserId CreatedBy { get; private set; }

    /// <summary>UTC instant at which the customer was last modified.</summary>
    public DateTime LastModifiedAt { get; private set; }

    /// <summary>Identifier of the actor that last modified this customer.</summary>
    public UserId LastModifiedBy { get; private set; }

    /// <summary>
    /// Creates an <see cref="CustomerStatus.Active"/> customer and stamps audit fields.
    /// </summary>
    /// <param name="id">Identifier of the new customer.</param>
    /// <param name="companyName">Company name; trimmed here.</param>
    /// <param name="vatNumber">VAT number in normal form, or <c>null</c>.</param>
    /// <param name="taxCode">Tax code in normal form, or <c>null</c>.</param>
    /// <param name="address">Address; trimmed here, empty becomes <c>null</c>.</param>
    /// <param name="clock">Time source.</param>
    /// <param name="createdBy">Actor performing the creation.</param>
    /// <exception cref="DomainException">
    /// Thrown with <see cref="CustomerErrors.CompanyNameInvalid"/> or
    /// <see cref="CustomerErrors.AddressInvalid"/> when an invariant is violated.
    /// </exception>
    public static Customer Create(
        CustomerId id,
        string? companyName,
        VatNumber? vatNumber,
        TaxCode? taxCode,
        string? address,
        IClock clock,
        UserId createdBy)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var trimmedName = ValidateCompanyName(companyName);
        var trimmedAddress = ValidateAddress(address);

        var now = clock.UtcNow;
        var customer = new Customer(id, trimmedName, vatNumber, taxCode, trimmedAddress, now, createdBy);
        customer.RaiseEvent(new CustomerCreated(
            Guid.NewGuid(), now, id, trimmedName, vatNumber, taxCode, trimmedAddress, customer.Status));
        return customer;
    }

    /// <summary>
    /// Replaces the editable data of the customer. A no-op when nothing changes: no event, no version
    /// bump, so no audit row.
    /// </summary>
    /// <param name="companyName">Company name; trimmed here.</param>
    /// <param name="vatNumber">VAT number in normal form, or <c>null</c>.</param>
    /// <param name="taxCode">Tax code in normal form, or <c>null</c>.</param>
    /// <param name="address">Address; trimmed here, empty becomes <c>null</c>.</param>
    /// <param name="clock">Time source.</param>
    /// <param name="modifiedBy">Actor performing the change.</param>
    /// <exception cref="DomainException">
    /// Thrown with <see cref="CustomerErrors.CompanyNameInvalid"/> or
    /// <see cref="CustomerErrors.AddressInvalid"/> when an invariant is violated.
    /// </exception>
    public void Update(
        string? companyName,
        VatNumber? vatNumber,
        TaxCode? taxCode,
        string? address,
        IClock clock,
        UserId modifiedBy)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var trimmedName = ValidateCompanyName(companyName);
        var trimmedAddress = ValidateAddress(address);

        if (CompanyName == trimmedName && VatNumber == vatNumber && TaxCode == taxCode
            && Address == trimmedAddress)
        {
            return;
        }

        CompanyName = trimmedName;
        VatNumber = vatNumber;
        TaxCode = taxCode;
        Address = trimmedAddress;

        var now = clock.UtcNow;
        Touch(now, modifiedBy);
        RaiseEvent(new CustomerUpdated(
            Guid.NewGuid(), now, Id, trimmedName, vatNumber, taxCode, trimmedAddress, Status));
    }

    /// <summary>
    /// Deactivates the customer. A no-op on an already deactivated customer: no event, no
    /// version bump.
    /// </summary>
    /// <param name="clock">Time source.</param>
    /// <param name="modifiedBy">Actor performing the change.</param>
    public void Deactivate(IClock clock, UserId modifiedBy)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Status is CustomerStatus.Deactivated)
        {
            return;
        }

        Status = CustomerStatus.Deactivated;

        var now = clock.UtcNow;
        Touch(now, modifiedBy);
        RaiseEvent(new CustomerDeactivated(Guid.NewGuid(), now, Id));
    }

    /// <summary>
    /// Reactivates the customer. A no-op on an already active customer: no event, no version bump.
    /// </summary>
    /// <param name="clock">Time source.</param>
    /// <param name="modifiedBy">Actor performing the change.</param>
    public void Reactivate(IClock clock, UserId modifiedBy)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Status is CustomerStatus.Active)
        {
            return;
        }

        Status = CustomerStatus.Active;

        var now = clock.UtcNow;
        Touch(now, modifiedBy);
        RaiseEvent(new CustomerReactivated(Guid.NewGuid(), now, Id));
    }

    private void Touch(DateTime now, UserId modifiedBy)
    {
        LastModifiedAt = now;
        LastModifiedBy = modifiedBy;
        IncrementVersion();
    }

    private static string ValidateCompanyName(string? companyName)
    {
        var trimmed = companyName?.Trim();

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > CompanyNameMaxLength)
        {
            throw new DomainException(CustomerErrors.CompanyNameInvalid);
        }

        return trimmed;
    }

    private static string? ValidateAddress(string? address)
    {
        var trimmed = address?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > AddressMaxLength)
        {
            throw new DomainException(CustomerErrors.AddressInvalid);
        }

        return trimmed;
    }
}
