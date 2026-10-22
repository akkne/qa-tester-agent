namespace QaAgent.Infrastructure.Persistence;

public sealed class RunRecord
{
    public Guid Id { get; set; }
    public required string RepositoryId { get; set; }
    public required string BaseSha { get; set; }
    public required string HeadSha { get; set; }
    public int ProfileVersion { get; set; }
    public required string State { get; set; }
    public string? FinalStatus { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? FailureReason { get; set; }
    public string? ChangedFilesJson { get; set; }
    public string? Diff { get; set; }
    public string? M2ResultJson { get; set; }
}
