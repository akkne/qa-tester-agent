using QaAgent.Application.Abstractions;

namespace QaAgent.Application.Analysis;

public sealed record CoverageMetric(int CoveredLines, int ExecutableLines, string? Reason = null)
{
    public bool IsMeasured => Reason is null && ExecutableLines > 0;

    public decimal? Percent => IsMeasured
        ? Math.Round(100m * CoveredLines / ExecutableLines, 2)
        : null;
}

public sealed record CoverageResult(CoverageMetric WholeProject, CoverageMetric ChangedLines);

public interface ICoverageCalculator
{
    Task<CoverageResult> CalculateAsync(
        GitSnapshot snapshot,
        IReadOnlyList<string> coberturaPaths,
        CancellationToken cancellationToken);
}
