using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.IntegrationTesting;

/// <summary>
/// Test stub for <see cref="IClock"/> that returns a fixed instant.
/// </summary>
public sealed class FixedClockStub : IClock
{
    private readonly DateTime _utcNow;

    public FixedClockStub(DateTime utcNow)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("FixedClockStub requires a UTC DateTime.", nameof(utcNow));
        }

        _utcNow = utcNow;
    }

    /// <inheritdoc />
    public DateTime UtcNow => _utcNow;

    /// <inheritdoc />
    public DateTimeOffset UtcNowOffset => new(_utcNow, TimeSpan.Zero);
}
