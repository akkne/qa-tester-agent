namespace QaAgent.Application.Analysis;

public static class M2Schemas
{
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
