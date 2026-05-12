namespace Priamus.Trips.SharedKernel.Money;

/// <summary>
/// ISO 4217 currency code as a value object. The <see cref="Code"/> is always
/// exactly three uppercase ASCII letters (A-Z); other values throw at construction.
/// </summary>
public readonly record struct Currency
{
    /// <summary>The Euro currency. Convenience constant since the Priamus Trips business runs in EUR.</summary>
    public static readonly Currency EUR = new("EUR");

    /// <summary>
    /// Creates a new <see cref="Currency"/> after validating ISO 4217 shape.
    /// </summary>
    /// <param name="code">Three uppercase ASCII letters.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="code"/> does not match ISO 4217 shape.</exception>
    public Currency(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (code.Length != 3)
        {
            throw new ArgumentException($"Currency code must be exactly 3 characters, got '{code}'.", nameof(code));
        }
        for (var i = 0; i < 3; i++)
        {
            var c = code[i];
            if (c is < 'A' or > 'Z')
            {
                throw new ArgumentException($"Currency code must be uppercase ASCII letters A-Z, got '{code}'.", nameof(code));
            }
        }
        Code = code;
    }

    /// <summary>The three-letter uppercase ISO 4217 code.</summary>
    public string Code { get; }

    /// <inheritdoc />
    public override string ToString() => Code;
}
