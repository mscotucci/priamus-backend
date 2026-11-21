using Priamus.Trips.Registry.Domain.Pricing;
using Priamus.Trips.SharedKernel.Domain;

namespace Priamus.Trips.Registry.Domain.NightBands.Events;

/// <summary>
/// Raised when the system night band is changed via <see cref="NightBandSetting.Update"/>.
/// Carries the band after the change.
/// </summary>
/// <param name="EventId">Fresh per-instance identifier of this event.</param>
/// <param name="OccurredAt">UTC instant of the change.</param>
/// <param name="SettingId">Identifier of the night band setting.</param>
/// <param name="Band">The night band after the change.</param>
public sealed record NightBandUpdated(
    Guid EventId,
    DateTime OccurredAt,
    Guid SettingId,
    NightBand Band) : IDomainEvent;
