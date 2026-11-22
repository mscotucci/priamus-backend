using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.Events;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.Domain.Customers.Events;
using Priamus.Trips.Registry.Domain.Customers.ValueObjects;
using Priamus.Trips.Registry.Domain.Drivers;
using Priamus.Trips.Registry.Domain.Drivers.Events;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.Domain.Locations.Events;
using Priamus.Trips.Registry.Domain.NightBands.Events;
using Priamus.Trips.Registry.Domain.Pricing;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.Domain.Vehicles.Events;
using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Security;

namespace Priamus.Trips.Registry.Persistence.Interceptors;

/// <summary>
/// EF Core <see cref="ISaveChangesInterceptor"/> that converts domain events buffered
/// on <see cref="AggregateRoot"/> instances into <see cref="AuditLogEntry"/> rows written
/// in the same transaction as the aggregate state change.
/// </summary>
/// <remarks>
/// Registered as <b>scoped</b> in DI (because it depends on <see cref="ICurrentUser"/>, which is
/// scoped) and added to the DbContext options in the DI registration extension. The event → entry
/// mapping is a switch expression: the module has nineteen event types. Created and updated rows carry
/// the full state after the change, with the plate, VAT number and tax code as plain strings, dates as <c>yyyy-MM-dd</c> and enums by name; the
/// payload is built explicitly instead of serializing the event, whose value objects would
/// otherwise appear as nested objects. <c>OccurredAt</c> is taken from the domain event, not
/// from a clock.
/// </remarks>
public sealed class AuditInterceptor : ISaveChangesInterceptor
{
    private static readonly JsonSerializerOptions SerializerOptions = AuditSerializerOptions.Default;

    private readonly ICurrentUser _currentUser;

    /// <summary>
    /// Initializes a new instance of <see cref="AuditInterceptor"/>.
    /// </summary>
    /// <param name="currentUser">The current-user abstraction; provides <c>actor_id</c> for audit rows.</param>
    public AuditInterceptor(ICurrentUser currentUser)
    {
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        AddAuditRows(eventData);
        return new ValueTask<InterceptionResult<int>>(result);
    }

    /// <inheritdoc />
    public InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        AddAuditRows(eventData);
        return result;
    }

    private void AddAuditRows(DbContextEventData eventData)
    {
        if (eventData.Context is null)
        {
            return;
        }

        var context = eventData.Context;
        Guid? actorId = _currentUser.IsAuthenticated ? _currentUser.UserId.Value : null;

        var aggregates = context.ChangeTracker
            .Entries<AggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                context.Set<AuditLogEntry>().Add(MapToAuditLogEntry(domainEvent, actorId));
            }

            aggregate.ClearDomainEvents();
        }
    }

    /// <summary>
    /// Maps a domain event to an <see cref="AuditLogEntry"/>. The <c>subject_id</c> is always
    /// the affected <c>VehicleId</c>, <c>CustomerId</c>, <c>LocationId</c>, <c>DriverId</c> or <c>ContractId</c>, or the id of the single night band row.
    /// </summary>
    internal static AuditLogEntry MapToAuditLogEntry(IDomainEvent domainEvent, Guid? actorId)
    {
        return domainEvent switch
        {
            VehicleCreated e => Create(
                e.EventId, e.OccurredAt, actorId, e.VehicleId.Value, AuditEventTypes.VehicleCreated,
                FullState(e.VehicleId.Value, e.LicensePlate.Value, e.Description, e.Type, e.RequiresTrailer, e.Status)),

            VehicleUpdated e => Create(
                e.EventId, e.OccurredAt, actorId, e.VehicleId.Value, AuditEventTypes.VehicleUpdated,
                FullState(e.VehicleId.Value, e.LicensePlate.Value, e.Description, e.Type, e.RequiresTrailer, e.Status)),

            VehicleDecommissioned e => Create(
                e.EventId, e.OccurredAt, actorId, e.VehicleId.Value, AuditEventTypes.VehicleDecommissioned,
                new { VehicleId = e.VehicleId.Value }),

            VehicleReactivated e => Create(
                e.EventId, e.OccurredAt, actorId, e.VehicleId.Value, AuditEventTypes.VehicleReactivated,
                new { VehicleId = e.VehicleId.Value }),

            CustomerCreated e => Create(
                e.EventId, e.OccurredAt, actorId, e.CustomerId.Value, AuditEventTypes.CustomerCreated,
                CustomerState(e.CustomerId, e.CompanyName, e.VatNumber, e.TaxCode, e.Address, e.Status)),

            CustomerUpdated e => Create(
                e.EventId, e.OccurredAt, actorId, e.CustomerId.Value, AuditEventTypes.CustomerUpdated,
                CustomerState(e.CustomerId, e.CompanyName, e.VatNumber, e.TaxCode, e.Address, e.Status)),

            CustomerDeactivated e => Create(
                e.EventId, e.OccurredAt, actorId, e.CustomerId.Value, AuditEventTypes.CustomerDeactivated,
                new { CustomerId = e.CustomerId.Value }),

            CustomerReactivated e => Create(
                e.EventId, e.OccurredAt, actorId, e.CustomerId.Value, AuditEventTypes.CustomerReactivated,
                new { CustomerId = e.CustomerId.Value }),

            LocationCreated e => Create(
                e.EventId, e.OccurredAt, actorId, e.LocationId.Value, AuditEventTypes.LocationCreated,
                LocationState(e.LocationId, e.Name, e.Municipality, e.Address, e.Status)),

            LocationUpdated e => Create(
                e.EventId, e.OccurredAt, actorId, e.LocationId.Value, AuditEventTypes.LocationUpdated,
                LocationState(e.LocationId, e.Name, e.Municipality, e.Address, e.Status)),

            LocationDeactivated e => Create(
                e.EventId, e.OccurredAt, actorId, e.LocationId.Value, AuditEventTypes.LocationDeactivated,
                new { LocationId = e.LocationId.Value }),

            LocationReactivated e => Create(
                e.EventId, e.OccurredAt, actorId, e.LocationId.Value, AuditEventTypes.LocationReactivated,
                new { LocationId = e.LocationId.Value }),

            DriverCreated e => Create(
                e.EventId, e.OccurredAt, actorId, e.DriverId.Value, AuditEventTypes.DriverCreated,
                DriverState(e.DriverId, e.FirstName, e.LastName, e.Phone, e.UserId, e.Status)),

            DriverUpdated e => Create(
                e.EventId, e.OccurredAt, actorId, e.DriverId.Value, AuditEventTypes.DriverUpdated,
                DriverState(e.DriverId, e.FirstName, e.LastName, e.Phone, e.UserId, e.Status)),

            DriverDeactivated e => Create(
                e.EventId, e.OccurredAt, actorId, e.DriverId.Value, AuditEventTypes.DriverDeactivated,
                new { DriverId = e.DriverId.Value }),

            DriverReactivated e => Create(
                e.EventId, e.OccurredAt, actorId, e.DriverId.Value, AuditEventTypes.DriverReactivated,
                new { DriverId = e.DriverId.Value }),

            ContractCreated e => Create(
                e.EventId, e.OccurredAt, actorId, e.ContractId.Value, AuditEventTypes.ContractCreated,
                ContractState(e.ContractId, e.CustomerId, e.ValidFrom, e.ValidTo, e.BillingLevel, e.PricingMode, e.RequiredDocuments, e.NightSurcharge, e.Rate, e.VehicleRates, e.LocationRates)),

            ContractUpdated e => Create(
                e.EventId, e.OccurredAt, actorId, e.ContractId.Value, AuditEventTypes.ContractUpdated,
                ContractState(e.ContractId, e.CustomerId, e.ValidFrom, e.ValidTo, e.BillingLevel, e.PricingMode, e.RequiredDocuments, e.NightSurcharge, e.Rate, e.VehicleRates, e.LocationRates)),

            NightBandUpdated e => Create(
                e.EventId, e.OccurredAt, actorId, e.SettingId, AuditEventTypes.NightBandUpdated,
                new
                {
                    SettingId = e.SettingId,
                    Start = TimeOfDayText.ToText(e.Band.Start),
                    End = TimeOfDayText.ToText(e.Band.End),
                }),

            _ => throw new UnreachableException(
                $"Unknown domain event type '{domainEvent.GetType().FullName}' reached the AuditInterceptor. " +
                "Add a case to the switch expression in AuditInterceptor.MapToAuditLogEntry.")
        };
    }

    private static object FullState(
        Guid vehicleId,
        string licensePlate,
        string description,
        VehicleType type,
        bool requiresTrailer,
        VehicleStatus status) =>
        new
        {
            VehicleId = vehicleId,
            LicensePlate = licensePlate,
            Description = description,
            Type = type,
            RequiresTrailer = requiresTrailer,
            Status = status,
        };

    private static object CustomerState(
        CustomerId customerId,
        string companyName,
        VatNumber? vatNumber,
        TaxCode? taxCode,
        string? address,
        CustomerStatus status) =>
        new
        {
            CustomerId = customerId.Value,
            CompanyName = companyName,
            VatNumber = vatNumber?.Value,
            TaxCode = taxCode?.Value,
            Address = address,
            Status = status,
        };

    private static object LocationState(
        LocationId locationId,
        string name,
        string municipality,
        string? address,
        LocationStatus status) =>
        new
        {
            LocationId = locationId.Value,
            Name = name,
            Municipality = municipality,
            Address = address,
            Status = status,
        };

    private static object DriverState(
        DriverId driverId,
        string firstName,
        string lastName,
        string? phone,
        UserId? userId,
        DriverStatus status) =>
        new
        {
            DriverId = driverId.Value,
            FirstName = firstName,
            LastName = lastName,
            Phone = phone,
            UserId = userId?.Value,
            Status = status,
        };

    private static object ContractState(
        ContractId contractId,
        CustomerId customerId,
        DateOnly validFrom,
        DateOnly? validTo,
        BillingLevel billingLevel,
        PricingMode pricingMode,
        IReadOnlyList<RequiredDocument> requiredDocuments,
        bool nightSurcharge,
        Rate? rate,
        IReadOnlyList<VehicleRate> vehicleRates,
        IReadOnlyList<LocationRate> locationRates) =>
        new
        {
            ContractId = contractId.Value,
            CustomerId = customerId.Value,
            ValidFrom = validFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ValidTo = validTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            BillingLevel = billingLevel,
            PricingMode = pricingMode,
            RequiredDocuments = requiredDocuments,
            NightSurcharge = nightSurcharge,
            DayPrice = rate?.DayPrice,
            NightPrice = rate?.NightPrice,
            NightCallFee = rate?.NightCallFee,
            MinimumRule = rate?.Minimum.Kind,
            MinimumAmount = rate?.Minimum.Amount,
            MinimumThreshold = rate?.Minimum.Threshold,
            MinimumOveragePrice = rate?.Minimum.OveragePrice,
            VehicleRates = vehicleRates.Count == 0
                ? null
                : vehicleRates.Select(v => new
                {
                    VehicleId = v.VehicleId.Value,
                    DayPrice = v.Rate.DayPrice,
                    NightPrice = v.Rate.NightPrice,
                    NightCallFee = v.Rate.NightCallFee,
                    MinimumRule = v.Rate.Minimum.Kind,
                    MinimumAmount = v.Rate.Minimum.Amount,
                    MinimumThreshold = v.Rate.Minimum.Threshold,
                    MinimumOveragePrice = v.Rate.Minimum.OveragePrice,
                }).ToArray(),
            LocationRates = locationRates.Count == 0
                ? null
                : locationRates.Select(l => new
                {
                    PickupLocationId = l.PickupLocationId?.Value,
                    DeliveryLocationId = l.DeliveryLocationId?.Value,
                    DayPrice = l.Rate.DayPrice,
                    NightPrice = l.Rate.NightPrice,
                    NightCallFee = l.Rate.NightCallFee,
                    MinimumRule = l.Rate.Minimum.Kind,
                    MinimumAmount = l.Rate.Minimum.Amount,
                    MinimumThreshold = l.Rate.Minimum.Threshold,
                    MinimumOveragePrice = l.Rate.Minimum.OveragePrice,
                }).ToArray(),
        };

    private static AuditLogEntry Create<TPayload>(
        Guid eventId,
        DateTime occurredAt,
        Guid? actorId,
        Guid subjectId,
        string eventType,
        TPayload payload)
    {
        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        return new AuditLogEntry(eventId, occurredAt, actorId, subjectId, eventType, json);
    }
}
