using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Identity.UnitTests.TestData;

/// <summary>
/// Deterministic IClock implementation for unit tests.
/// Accepts an initial UTC instant and can be advanced manually.
/// </summary>
internal sealed class FakeClock : IClock
{
    private DateTime _utcNow;

    public FakeClock(DateTime utcNow)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("FakeClock requires a UTC DateTime.", nameof(utcNow));
        }

        _utcNow = utcNow;
    }

    /// <inheritdoc />
    public DateTime UtcNow => _utcNow;

    /// <inheritdoc />
    public DateTimeOffset UtcNowOffset => new(_utcNow, TimeSpan.Zero);

    /// <summary>Advances the clock by the given duration.</summary>
    public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
}
