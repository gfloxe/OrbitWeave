using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrbitWeave.Actions;

public static class ActionJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        AllowOutOfOrderMetadataProperties = true,
        Converters = { new JsonStringEnumConverter() }
    };
}
