namespace QaAgent.Infrastructure.Git;

public sealed class GitWorkspaceOptions
{
    public required string Source { get; init; }
    public required string WorkspaceRoot { get; init; }
}
