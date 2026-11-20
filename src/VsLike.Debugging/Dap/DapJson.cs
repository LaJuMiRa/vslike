using System.Text.Json;
using System.Text.Json.Serialization;

namespace VsLike.Debugging.Dap;

internal static class DapJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
