using System.Text.Json;
using QaAgent.Application.Analysis;

namespace QaAgent.UnitTests;

public sealed class M2SchemasTests
{
    [Theory]
    [MemberData(nameof(Schemas))]
    public void StructuredOutputAllowsOnlyKnownRequirementIds(string schema)
    {
        string constrained = M2Schemas.WithRequirementIds(schema, ["DEMO-REG-07", "DEMO-REG-01"]);
        using JsonDocument parsed = JsonDocument.Parse(constrained);
        string[] allowed = ["DEMO-REG-01", "DEMO-REG-07"];
        JsonElement item = parsed.RootElement.GetProperty("properties").EnumerateObject().First().Value
            .GetProperty("items").GetProperty("properties");
        JsonElement id = item.TryGetProperty("requirementId", out JsonElement single)
            ? single : item.GetProperty("requirementIds").GetProperty("items");

        Assert.Equal(allowed, id.GetProperty("enum").EnumerateArray().Select(value => value.GetString()).ToArray());
    }

    public static IEnumerable<object[]> Schemas()
    {
        yield return [M2Schemas.Plan];
        yield return [M2Schemas.Findings];
    }
}
