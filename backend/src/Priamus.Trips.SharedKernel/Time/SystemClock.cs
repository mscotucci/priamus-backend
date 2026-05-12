namespace Priamus.Trips.SharedKernel.Time;

/// <summary>
/// Default <see cref="IClock"/> implementation that delegates to the system clock.
/// This is the ONLY place in the entire backend where <see cref="DateTime.UtcNow"/>
/// and <see cref="DateTimeOffset.UtcNow"/> are allowed.
/// </summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTime UtcNow => DateTime.UtcNow;

    /// <inheritdoc />
    public DateTimeOffset UtcNowOffset => DateTimeOffset.UtcNow;
}
