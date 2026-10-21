namespace QaAgent.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
