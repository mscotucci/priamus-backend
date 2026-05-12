namespace Priamus.Trips.SharedKernel.Time;

/// <summary>
/// Abstraction over wall-clock time. Domain code, application services, and
/// handlers must consume time through this interface — never via
/// <see cref="DateTime.UtcNow"/> or <see cref="DateTimeOffset.UtcNow"/> directly.
/// The single exception is <see cref="SystemClock"/>, which is the production
/// implementation.
/// </summary>
public interface IClock
{
    /// <summary>Current UTC instant.</summary>
    DateTime UtcNow { get; }

    /// <summary>Current UTC instant with offset.</summary>
    DateTimeOffset UtcNowOffset { get; }
}
