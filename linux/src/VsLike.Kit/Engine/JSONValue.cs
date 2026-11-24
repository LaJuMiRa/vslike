using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace VsLike.Kit;

/// <summary>
/// JSON settings of the engine protocol (same as engine/src/VsLike.Engine/Rpc/RpcJson.cs): camelCase names, enums as
/// camelCase strings, flag enums as numbers, nulls omitted. The Swift original has its own <c>JSONValue</c> enum for
/// inline parameters; in C# <see cref="JsonNode"/> and anonymous objects fill that role.
/// </summary>
public static class JSONValue
{
    public static readonly JsonSerializerOptions Options = Create();

    /// <summary>Wraps any value as a JSON node (encoded with the protocol's settings).</summary>
    public static JsonNode? Encode(object? value) => JsonSerializer.SerializeToNode(value, Options);

    /// <summary>Decodes a JSON node into a type with the protocol's settings.</summary>
    public static T? Decode<T>(JsonNode? node) => node is null ? default : node.Deserialize<T>(Options);

    public static string? StringValue(this JsonNode? node, string key) =>
        node is JsonObject obj && obj[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    public static int? IntValue(this JsonNode? node, string key)
    {
        if (node is not JsonObject obj || obj[key] is not JsonValue v)
        {
            return null;
        }

        if (v.TryGetValue<int>(out var i))
        {
            return i;
        }

        return v.TryGetValue<double>(out var d) ? (int)d : null;
    }

    public static bool? BoolValue(this JsonNode? node, string key) =>
        node is JsonObject obj && obj[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    public static JsonArray ArrayValue(this JsonNode? node, string key) =>
        node is JsonObject obj && obj[key] is JsonArray a ? a : [];

    public static JsonNode? Child(this JsonNode? node, string key) => node is JsonObject obj ? obj[key] : null;

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };
        options.Converters.Add(new JsonNumberEnumConverter<TypeModifiers>());
        options.Converters.Add(new JsonNumberEnumConverter<MemberModifiers>());
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
