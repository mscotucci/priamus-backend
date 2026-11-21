using Priamus.Trips.Registry.Domain.NightBands.Events;
using Priamus.Trips.Registry.Domain.Pricing;
using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Registry.Domain.NightBands;

/// <summary>
/// The system night band (PRD §5.8): a single row, seeded by the migration with
/// <see cref="NightBand.Default"/>, changed by an administrator and versioned for optimistic
/// concurrency. It has no validity dates: the band in force is the one stored now.
/// </summary>
public sealed class NightBandSetting : AggregateRoot
{
    /// <summary>Identifier of the one row.</summary>
    public static readonly Guid SingleId = new("00000000-0000-0000-0000-000000000001");

    private TimeOnly _start;
    private TimeOnly _end;

    private NightBandSetting()
    {
        // EF only. Members are populated by EF Core via property setters/backing fields.
    }

    private NightBandSetting(NightBand band, DateTime createdAt, UserId createdBy)
    {
        Id = SingleId;
        Band = band;
        LastModifiedAt = createdAt;
        LastModifiedBy = createdBy;
    }

    /// <summary>Identifier of the one row, always <see cref="SingleId"/>.</summary>
    public Guid Id { get; private set; }

    /// <summary>The night band in force.</summary>
    public NightBand Band
    {
        get => NightBand.Create(_start, _end).Value;
        private set
        {
            _start = value.Start;
            _end = value.End;
        }
    }

    /// <summary>UTC instant at which the band was last modified.</summary>
    public DateTime LastModifiedAt { get; private set; }

    /// <summary>Identifier of the actor that last modified the band.</summary>
    public UserId LastModifiedBy { get; private set; }

    /// <summary>
    /// Builds the setting as the migration seeds it. Raises no event: the seed is not an audited change.
    /// </summary>
    /// <param name="band">The initial band.</param>
    /// <param name="clock">Time source.</param>
    /// <param name="createdBy">Actor creating the setting.</param>
    public static NightBandSetting Create(NightBand band, IClock clock, UserId createdBy)
    {
        ArgumentNullException.ThrowIfNull(band);
        ArgumentNullException.ThrowIfNull(clock);

        return new NightBandSetting(band, clock.UtcNow, createdBy);
    }

    /// <summary>
    /// Replaces the night band. A no-op when the band is unchanged: no event, no version bump, so
    /// no audit row.
    /// </summary>
    /// <param name="band">The new band.</param>
    /// <param name="clock">Time source.</param>
    /// <param name="modifiedBy">Actor performing the change.</param>
    public void Update(NightBand band, IClock clock, UserId modifiedBy)
    {
        ArgumentNullException.ThrowIfNull(band);
        ArgumentNullException.ThrowIfNull(clock);

        if (Band == band)
        {
            return;
        }

        Band = band;

        var now = clock.UtcNow;
        LastModifiedAt = now;
        LastModifiedBy = modifiedBy;
        IncrementVersion();
        RaiseEvent(new NightBandUpdated(Guid.NewGuid(), now, Id, band));
    }
}
