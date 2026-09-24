using System.Text.Json;
using System.Text.Json.Serialization;

namespace NpApi.Api.Tests.Infrastructure;

// Matches the API's JSON settings (web defaults, enums as strings).
public static class TestJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
