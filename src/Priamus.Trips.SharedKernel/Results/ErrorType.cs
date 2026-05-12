namespace Priamus.Trips.SharedKernel.Results;

/// <summary>
/// Classification of an <see cref="Error"/>. Drives the HTTP status code mapping
/// in <see cref="ResultHttpExtensions"/>. Adding a new <see cref="ErrorType"/> value
/// requires extending the mapping; adding a new <see cref="Error"/> inside a module
/// does not.
/// </summary>
public enum ErrorType
{
    /// <summary>Sentinel for the success case. Not a real error.</summary>
    None = 0,

    /// <summary>Input error beyond attribute validation, structural issue. Maps to HTTP 400.</summary>
    Validation = 1,

    /// <summary>Entity does not exist. Maps to HTTP 404.</summary>
    NotFound = 2,

    /// <summary>State conflict, uniqueness, concurrency, edit window expired. Maps to HTTP 409.</summary>
    Conflict = 3,

    /// <summary>Authenticated but not allowed at business level. Maps to HTTP 403.</summary>
    Forbidden = 4,

    /// <summary>Authentication missing or invalid. Maps to HTTP 401.</summary>
    Unauthorized = 5,

    /// <summary>Business invariant violated. Maps to HTTP 422.</summary>
    Domain = 6,

    /// <summary>Truly unanticipated error. Maps to HTTP 500.</summary>
    Unexpected = 7,
}
