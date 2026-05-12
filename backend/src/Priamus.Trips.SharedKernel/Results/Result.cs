namespace Priamus.Trips.SharedKernel.Results;

/// <summary>
/// Outcome of an operation that has no payload on success.
/// <see cref="Result"/> is a <c>readonly struct</c> to avoid heap allocation on the common success path.
/// </summary>
public readonly struct Result
{
    private readonly Error _error;

    private Result(bool isSuccess, Error error)
    {
        IsSuccess = isSuccess;
        _error = error;
    }

    /// <summary>True when the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>True when the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>The error describing the failure. Returns <see cref="Priamus.Trips.SharedKernel.Results.Error.None"/> for a successful result.</summary>
    public Error Error => IsSuccess ? global::Priamus.Trips.SharedKernel.Results.Error.None : _error;

    /// <summary>Creates a successful result.</summary>
    public static Result Success() => new(true, global::Priamus.Trips.SharedKernel.Results.Error.None);

    /// <summary>
    /// Creates a failed result. The provided <paramref name="error"/> must not be <see cref="Priamus.Trips.SharedKernel.Results.Error.None"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="error"/> is <see cref="Priamus.Trips.SharedKernel.Results.Error.None"/>.</exception>
    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (error.Type == ErrorType.None)
        {
            throw new InvalidOperationException("Cannot build a failed Result from Error.None.");
        }
        return new Result(false, error);
    }

    /// <summary>Implicit conversion from an <see cref="Error"/> to a failed <see cref="Result"/>.</summary>
    public static implicit operator Result(Error error) => Failure(error);
}
