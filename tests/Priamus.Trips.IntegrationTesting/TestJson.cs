using System.Text.Json;
using System.Text.Json.Serialization;

namespace Priamus.Trips.IntegrationTesting;

/// <summary>
/// The JSON conventions of the API as a client sees them: web defaults plus enums by name.
/// <c>HttpClient</c>'s own defaults know nothing of the host's enum converter, so a typed DTO
/// carrying an enum must be sent and read with these options.
/// </summary>
public static class TestJson
{
    /// <summary>Options matching the host's HTTP JSON options.</summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };
}
