using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.SharedKernel.Domain;

/// <summary>
/// Exception thrown by value objects and aggregates when a domain invariant is
/// violated. Handlers catch this exception and convert it to a
/// <see cref="Result{T}"/> failure carrying the same <see cref="ErrorCode"/>
/// and message.
/// </summary>
public sealed class DomainException : Exception
{
    /// <summary>
    /// Creates a new <see cref="DomainException"/> from a domain <see cref="Error"/>.
    /// The <see cref="Error.Code"/> is preserved on <see cref="ErrorCode"/> and the
    /// <see cref="Error.Description"/> becomes the exception message.
    /// </summary>
    /// <param name="error">The error describing the violated invariant.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is null.</exception>
    public DomainException(Error error)
        : base((error ?? throw new ArgumentNullException(nameof(error))).Description)
    {
        ErrorCode = error.Code;
    }

    /// <summary>The machine-readable error code, e.g. <c>"Trip.TrailerRequired"</c>.</summary>
    public string ErrorCode { get; }
}
