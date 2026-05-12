namespace Priamus.Trips.SharedKernel.Results;

/// <summary>
/// A typed error returned by handlers and value-object factories. Errors carry a
/// machine-readable <see cref="Code"/> (e.g. <c>"Trip.TrailerRequired"</c>),
/// a developer-facing English <see cref="Description"/>, and a semantic
/// <see cref="Type"/> used to derive the HTTP status code.
/// </summary>
/// <param name="Code">Stable identifier in <c>{Context}.{ErrorName}</c> PascalCase form.</param>
/// <param name="Description">Developer-facing English description; never a localized user message.</param>
/// <param name="Type">Semantic classification driving HTTP status mapping.</param>
public sealed record Error(string Code, string Description, ErrorType Type)
{
    /// <summary>Sentinel value representing the success case. Use only as a placeholder; never return as a failure.</summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.None);

    /// <summary>Builds a <see cref="ErrorType.Validation"/> error.</summary>
    public static Error Validation(string code, string description) => new(code, description, ErrorType.Validation);

    /// <summary>Builds a <see cref="ErrorType.NotFound"/> error.</summary>
    public static Error NotFound(string code, string description) => new(code, description, ErrorType.NotFound);

    /// <summary>Builds a <see cref="ErrorType.Conflict"/> error.</summary>
    public static Error Conflict(string code, string description) => new(code, description, ErrorType.Conflict);

    /// <summary>Builds a <see cref="ErrorType.Forbidden"/> error.</summary>
    public static Error Forbidden(string code, string description) => new(code, description, ErrorType.Forbidden);

    /// <summary>Builds a <see cref="ErrorType.Unauthorized"/> error.</summary>
    public static Error Unauthorized(string code, string description) => new(code, description, ErrorType.Unauthorized);

    /// <summary>Builds a <see cref="ErrorType.Domain"/> error (the most common kind in domain code).</summary>
    public static Error Domain(string code, string description) => new(code, description, ErrorType.Domain);

    /// <summary>Builds a <see cref="ErrorType.Unexpected"/> error. Use sparingly.</summary>
    public static Error Unexpected(string code, string description) => new(code, description, ErrorType.Unexpected);
}
