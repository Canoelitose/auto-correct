using System.Text.Json.Serialization;

namespace AutoCorrect.Core.Engines.LanguageTool;

/// <summary>Response shape of POST /v2/check, reduced to the fields we use.</summary>
internal sealed class LtResponse
{
    [JsonPropertyName("matches")]
    public List<LtMatch>? Matches { get; set; }
}

internal sealed class LtMatch
{
    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("length")]
    public int Length { get; set; }

    [JsonPropertyName("replacements")]
    public List<LtReplacement>? Replacements { get; set; }

    [JsonPropertyName("rule")]
    public LtRule? Rule { get; set; }
}

internal sealed class LtReplacement
{
    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

internal sealed class LtRule
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("issueType")]
    public string? IssueType { get; set; }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(LtResponse))]
internal sealed partial class LanguageToolJsonContext : JsonSerializerContext
{
}
