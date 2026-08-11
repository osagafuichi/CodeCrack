using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeCrack.App.Core.Engine;

/// Central, shared serializer options for the engine JSON contract.
/// snake_case names (finding_id, test_name, by_outcome) map to PascalCase members;
/// unmapped members are rejected (a shape change fails loudly) except where a type
/// opts out via [JsonUnmappedMemberHandling(Skip)].
public static class EngineJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Disallow,
    };
}
