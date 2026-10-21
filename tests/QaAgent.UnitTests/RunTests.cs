using QaAgent.Domain;

namespace QaAgent.UnitTests;

public sealed class RunTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 21, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_run_moves_from_queued_to_running_to_completed()
    {
        Run run = CreateRun();

        run.Start(CreatedAt.AddMinutes(1));
        run.Complete(RunStatus.Failed, CreatedAt.AddMinutes(2));

        Assert.Equal(RunState.Completed, run.State);
        Assert.Equal(RunStatus.Failed, run.FinalStatus);
        Assert.Equal(CreatedAt.AddMinutes(1), run.StartedAt);
        Assert.Equal(CreatedAt.AddMinutes(2), run.FinishedAt);
    }

    [Fact]
    public void A_run_cannot_complete_before_it_starts()
    {
        Run run = CreateRun();

        Assert.Throws<InvalidOperationException>(() => run.Complete(RunStatus.Passed, CreatedAt));
    }

    [Fact]
    public void A_run_cannot_restart()
    {
        Run run = CreateRun();
        run.Start(CreatedAt);

        Assert.Throws<InvalidOperationException>(() => run.Start(CreatedAt.AddMinutes(1)));
    }

    [Fact]
    public void A_run_rejects_equal_base_and_head()
    {
        CommitSha sha = CommitSha.Parse(new string('a', 40));

        Assert.Throws<ArgumentException>(() => Run.Queue(
            Guid.NewGuid(), "owner/repo", sha, sha, 1, CreatedAt));
    }

    private static Run CreateRun() => Run.Queue(
        Guid.NewGuid(),
        "owner/repo",
        CommitSha.Parse(new string('a', 40)),
        CommitSha.Parse(new string('b', 40)),
        1,
        CreatedAt);
}
