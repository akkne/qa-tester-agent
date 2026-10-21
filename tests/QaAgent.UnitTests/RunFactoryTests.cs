using QaAgent.Application;
using QaAgent.Application.Abstractions;
using QaAgent.Domain;

namespace QaAgent.UnitTests;

public sealed class RunFactoryTests
{
    [Fact]
    public void Factory_uses_the_injected_clock()
    {
        DateTimeOffset fixedNow = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        RunFactory factory = new(new FixedClock(fixedNow));

        Run run = factory.Create(
            Guid.NewGuid(),
            "owner/repo",
            CommitSha.Parse(new string('a', 40)),
            CommitSha.Parse(new string('b', 40)),
            1);

        Assert.Equal(fixedNow, run.CreatedAt);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
