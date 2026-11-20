using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskManager.API.Infrastructure;

/// <summary>One JSON convention for REST, OpenAPI and SignalR: enums as names, numbers only as numbers.</summary>
public static class JsonConfig
{
    public static void Apply(JsonSerializerOptions options)
    {
        options.Converters.Add(new JsonStringEnumConverter());
        options.NumberHandling = JsonNumberHandling.Strict;
    }
}
