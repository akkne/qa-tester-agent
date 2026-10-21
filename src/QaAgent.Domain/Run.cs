namespace QaAgent.Domain;

public sealed class Run
{
    public Guid Id { get; }
    public string RepositoryId { get; }
    public CommitSha BaseSha { get; }
    public CommitSha HeadSha { get; }
    public int ProfileVersion { get; }
    public RunState State { get; private set; }
    public RunStatus? FinalStatus { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    private Run(
        Guid id,
        string repositoryId,
        CommitSha baseSha,
        CommitSha headSha,
        int profileVersion,
        DateTimeOffset createdAt)
    {
        Id = id;
        RepositoryId = repositoryId;
        BaseSha = baseSha;
        HeadSha = headSha;
        ProfileVersion = profileVersion;
        CreatedAt = createdAt;
        State = RunState.Queued;
    }

    public static Run Queue(
        Guid id,
        string repositoryId,
        CommitSha baseSha,
        CommitSha headSha,
        int profileVersion,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A run ID is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(repositoryId))
        {
            throw new ArgumentException("A repository ID is required.", nameof(repositoryId));
        }

        ArgumentNullException.ThrowIfNull(baseSha);
        ArgumentNullException.ThrowIfNull(headSha);

        if (baseSha == headSha)
        {
            throw new ArgumentException("Base and head SHA must differ.", nameof(headSha));
        }

        if (profileVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(profileVersion));
        }

        return new Run(id, repositoryId, baseSha, headSha, profileVersion, createdAt);
    }

    public void Start(DateTimeOffset now)
    {
        if (State != RunState.Queued)
        {
            throw new InvalidOperationException("Only a queued run can start.");
        }

        if (now < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(now));
        }

        State = RunState.Running;
        StartedAt = now;
    }

    public void Complete(RunStatus status, DateTimeOffset now)
    {
        if (State != RunState.Running)
        {
            throw new InvalidOperationException("Only a running run can complete.");
        }

        if (now < StartedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(now));
        }

        State = RunState.Completed;
        FinalStatus = status;
        FinishedAt = now;
    }
}
