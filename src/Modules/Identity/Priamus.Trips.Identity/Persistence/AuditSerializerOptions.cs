using System.Text.Json;
using System.Text.Json.Serialization;

namespace Priamus.Trips.Identity.Persistence;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> used when serializing audit-log payloads.
/// Centralizes the options so that <see cref="Interceptors.AuditInterceptor"/> and
/// handlers writing non-domain audit rows use an identical format.
/// </summary>
internal static class AuditSerializerOptions
{
    /// <summary>
    /// camelCase, no null properties. Matches the format used by the legacy
    /// <see cref="Interceptors.AuditInterceptor"/> that was already in place when
    /// this shared location was introduced.
    /// </summary>
    internal static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
