using QaAgent.Domain;

namespace QaAgent.UnitTests;

public sealed class CommitShaTests
{
    [Fact]
    public void Parse_normalizes_hex_to_lowercase()
    {
        CommitSha sha = CommitSha.Parse(new string('A', 40));

        Assert.Equal(new string('a', 40), sha.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void Parse_rejects_invalid_sha(string value)
    {
        Assert.Throws<ArgumentException>(() => CommitSha.Parse(value));
    }
}
