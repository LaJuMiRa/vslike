using System.Text.Json;
using System.Text.Json.Serialization;
using VsLike.Diagram.Model;

namespace VsLike.Engine.Rpc;

/// <summary>
/// JSON settings of the protocol: camelCase names, enums as camelCase strings, flag enums as numbers (they map to
/// option sets in Swift), nulls omitted.
/// </summary>
public static class RpcJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonNumberEnumConverter<TypeModifiers>());
        options.Converters.Add(new JsonNumberEnumConverter<MemberModifiers>());
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
