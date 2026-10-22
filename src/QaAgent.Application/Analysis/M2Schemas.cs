using System.Text.Json.Nodes;

namespace QaAgent.Application.Analysis;

public static class M2Schemas
{
    public static string WithRequirementIds(string schema, IReadOnlyCollection<string> requirementIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        if (requirementIds.Count == 0)
            throw new ArgumentException("At least one requirement ID is required.", nameof(requirementIds));

        JsonNode root = JsonNode.Parse(schema) ?? throw new ArgumentException("Invalid JSON schema.", nameof(schema));
        Apply(root);
        return root.ToJsonString();

        void Apply(JsonNode node)
        {
            if (node is JsonObject properties && properties["requirementId"] is JsonObject id)
                id["enum"] = Ids();
            if (node is JsonObject arrays && arrays["requirementIds"] is JsonObject ids && ids["items"] is JsonObject item)
                item["enum"] = Ids();
            if (node is JsonObject obj)
            {
                foreach (JsonNode? child in obj.Select(pair => pair.Value).ToArray())
                    if (child is not null) Apply(child);
            }
            else if (node is JsonArray array)
            {
                foreach (JsonNode? child in array.ToArray())
                    if (child is not null) Apply(child);
            }
        }

        JsonArray Ids() => new(requirementIds.Order(StringComparer.Ordinal)
            .Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
    }

    public const string Plan = """
        {"type":"object","additionalProperties":false,"required":["scenarios","contextGaps"],"properties":{"scenarios":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["requirementId","kind","expected","steps"],"properties":{"requirementId":{"type":"string"},"kind":{"type":"string","enum":["positive","negative","boundary","access-control"]},"expected":{"type":"string"},"steps":{"type":"string"}}}},"contextGaps":{"type":"array","items":{"type":"string"}}}}
        """;

    public const string Findings = """
        {"type":"object","additionalProperties":false,"required":["findings"],"properties":{"findings":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["requirementId","path","line","severity","expected","observed","reproduction","status"],"properties":{"requirementId":{"type":"string"},"path":{"type":"string"},"line":{"type":"integer"},"severity":{"type":"string","enum":["low","medium","high","critical"]},"expected":{"type":"string"},"observed":{"type":"string"},"reproduction":{"type":"string"},"status":{"type":"string","enum":["hypothesis","reproduced"]}}}}}}
        """;

    public const string Tests = """
        {"type":"object","additionalProperties":false,"required":["tests"],"properties":{"tests":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["path","requirementIds","kind","source"],"properties":{"path":{"type":"string"},"requirementIds":{"type":"array","items":{"type":"string"}},"kind":{"type":"string","enum":["positive","negative","boundary","access-control"]},"source":{"type":"string"}}}}}}
        """;
}
