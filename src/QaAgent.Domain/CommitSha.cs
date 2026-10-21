namespace QaAgent.Domain;

public sealed record CommitSha
{
    public string Value { get; }

    private CommitSha(string value) => Value = value;

    public static CommitSha Parse(string value)
    {
        if (value is null || value.Length != 40)
        {
            throw new ArgumentException("A commit SHA must contain 40 hexadecimal characters.", nameof(value));
        }

        foreach (char character in value)
        {
            if (!Uri.IsHexDigit(character))
            {
                throw new ArgumentException("A commit SHA must contain 40 hexadecimal characters.", nameof(value));
            }
        }

        return new CommitSha(value.ToLowerInvariant());
    }

    public override string ToString() => Value;
}
