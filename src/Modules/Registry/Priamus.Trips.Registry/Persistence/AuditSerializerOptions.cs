using System.Text.Json;
using System.Text.Json.Serialization;

namespace Priamus.Trips.Registry.Persistence;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> used when serializing audit-log payloads.
/// Centralizes the options so that every audit row of the module uses an identical format.
/// </summary>
internal static class AuditSerializerOptions
{
    /// <summary>
    /// camelCase, no null properties, enums by member name (never as numbers).
    /// </summary>
    internal static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };
}
