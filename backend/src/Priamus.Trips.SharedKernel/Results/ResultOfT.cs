namespace Priamus.Trips.SharedKernel.Results;

/// <summary>
/// Outcome of an operation that produces a value of type <typeparamref name="T"/> on success.
/// <see cref="Result{T}"/> is a <c>readonly struct</c> to avoid heap allocation on the common success path.
/// </summary>
/// <typeparam name="T">The payload type returned on success.</typeparam>
public readonly struct Result<T>
{
    private readonly T _value;
    private readonly Error _error;

    private Result(T value, bool isSuccess, Error error)
    {
        _value = value;
        _error = error;
        IsSuccess = isSuccess;
    }

    private static Error NoneError => global::Priamus.Trips.SharedKernel.Results.Error.None;

    /// <summary>True when the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>True when the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Returns the success payload. Throws when the result is a failure.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when accessed on a failed result.</exception>
    public T Value => IsSuccess
        ? _value
        : throw new InvalidOperationException("Cannot access Value on a failed Result.");

    /// <summary>The error describing the failure. Returns <see cref="Priamus.Trips.SharedKernel.Results.Error.None"/> for a successful result.</summary>
    public Error Error => IsSuccess ? NoneError : _error;

    /// <summary>Creates a successful result wrapping <paramref name="value"/>.</summary>
    public static Result<T> Success(T value) => new(value, true, NoneError);

    /// <summary>
    /// Creates a failed result. The provided <paramref name="error"/> must not be <see cref="Priamus.Trips.SharedKernel.Results.Error.None"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="error"/> is <see cref="Priamus.Trips.SharedKernel.Results.Error.None"/>.</exception>
    public static Result<T> Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (error.Type == ErrorType.None)
        {
            throw new InvalidOperationException("Cannot build a failed Result<T> from Error.None.");
        }
        return new Result<T>(default!, false, error);
    }

    /// <summary>
    /// Pattern-matches on success or failure to produce a value of type <typeparamref name="TOut"/>.
    /// </summary>
    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return IsSuccess ? onSuccess(_value) : onFailure(_error);
    }

    /// <summary>
    /// Maps the success payload via <paramref name="mapper"/>. The error is propagated unchanged on failure.
    /// </summary>
    public Result<TOut> Map<TOut>(Func<T, TOut> mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        return IsSuccess ? Result<TOut>.Success(mapper(_value)) : Result<TOut>.Failure(_error);
    }

    /// <summary>
    /// Chains another <see cref="Result{TOut}"/>-returning operation. Short-circuits on failure.
    /// </summary>
    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> binder)
    {
        ArgumentNullException.ThrowIfNull(binder);
        return IsSuccess ? binder(_value) : Result<TOut>.Failure(_error);
    }

    /// <summary>
    /// Asynchronously chains another <see cref="Result{TOut}"/>-returning operation. Short-circuits on failure.
    /// </summary>
    public async Task<Result<TOut>> BindAsync<TOut>(Func<T, Task<Result<TOut>>> binder)
    {
        ArgumentNullException.ThrowIfNull(binder);
        return IsSuccess ? await binder(_value).ConfigureAwait(false) : Result<TOut>.Failure(_error);
    }

    /// <summary>Implicit conversion from a payload to a successful result.</summary>
    public static implicit operator Result<T>(T value) => Success(value);

    /// <summary>Implicit conversion from an <see cref="Error"/> to a failed result.</summary>
    public static implicit operator Result<T>(Error error) => Failure(error);
}
