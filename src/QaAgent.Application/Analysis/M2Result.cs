using QaAgent.Application.Abstractions;

namespace QaAgent.Application.Analysis;

public sealed record VerificationScenario(
    string RequirementId,
    string Kind,
    string Expected,
    string Steps);

public sealed record AnalysisFinding(
    string RequirementId,
    string Path,
    int Line,
    string Severity,
    string Expected,
    string Observed,
    string Reproduction,
    string Status);

public sealed record GeneratedTest(
    string Path,
    IReadOnlyList<string> RequirementIds,
    string Kind,
    string Source);

public sealed record M2Result(
    IReadOnlyList<string> RequirementIds,
    IReadOnlyList<VerificationScenario> Plan,
    IReadOnlyList<AnalysisFinding> Findings,
    IReadOnlyList<GeneratedTest> Tests,
    IReadOnlyList<string> ContextGaps,
    int InputTokens,
    int OutputTokens,
    int GenerationFixes,
    bool TestsCompile,
    string? BuildDiagnostics,
    IReadOnlyList<SecretFinding> SecretFindings);
