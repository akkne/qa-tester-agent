using System.Text.Json;
using QaAgent.Domain;

namespace QaAgent.Application.Execution;

public sealed record M3Outcome(RunStatus Status, string? Reason)
{
    private static readonly string[] RequiredChecks = ["restore", "format-whitespace", "format-style", "build", "test"];
    private static readonly string[] LeadQualificationChecks = ["restore", "build", "test", "host-smoke"];

    public static M3Outcome From(M3Result result)
    {
        if (result.Profile == M3ExecutionProfile.LeadQualification)
        {
            if (LeadQualificationChecks.Any(name => !result.Checks.TryGetValue(name, out StageCheckResult? check) ||
                    check.ExitCode == 125 || string.IsNullOrWhiteSpace(check.LogPath)))
                return new M3Outcome(RunStatus.Blocked, "M3 bot checks or their logs were unavailable.");
            // Restore depends on NuGet and the network, so its failure says nothing about the code.
            if (result.Checks["restore"] is { Passed: false } restore)
                return new M3Outcome(RunStatus.Blocked, "M3 package restore failed: " + restore.Summary[^Math.Min(restore.Summary.Length, 500)..]);
            // A real build or test failure is a verdict on the code, even when no test counts exist.
            if (LeadQualificationChecks.Any(name => !result.Checks[name].Passed))
                return new M3Outcome(RunStatus.Failed, null);
            if (result.TotalTests is null or 0 || result.FailedTests is null)
                return new M3Outcome(RunStatus.Blocked, "M3 bot test results were not measured.");
            return LeadQualificationChecks.All(name =>
                    result.Checks[name].Passed && result.Checks[name].ExitCode == 0) && result.FailedTests == 0
                ? new M3Outcome(RunStatus.Passed, null)
                : new M3Outcome(RunStatus.Failed, null);
        }

        if (!result.StandAvailable)
            return new M3Outcome(RunStatus.Blocked, result.StandFailure ?? "M3 stand was unavailable.");
        if (result.BrowserPassed is null || string.IsNullOrWhiteSpace(result.BrowserJson))
            return new M3Outcome(RunStatus.Blocked, "M3 browser runner was unavailable.");
        if (RequiredChecks.Any(name => !result.Checks.ContainsKey(name)) ||
            result.Checks.Values.Any(check => check.ExitCode == 125))
            return new M3Outcome(RunStatus.Blocked, "M3 checks could not run in Docker.");
        if (result.TotalTests is null or 0 || result.Coverage is null ||
            !result.Coverage.WholeProject.IsMeasured)
            return new M3Outcome(RunStatus.Blocked, "M3 test or Coverlet results were not measured.");

        string? browserStatus;
        try
        {
            using JsonDocument document = JsonDocument.Parse(result.BrowserJson);
            browserStatus = document.RootElement.GetProperty("status").GetString();
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new M3Outcome(RunStatus.Blocked, "M3 browser result was invalid.");
        }

        if (browserStatus is not ("PASSED" or "FAILED"))
            return new M3Outcome(RunStatus.Blocked, "M3 browser checks were blocked or invalid.");
        return result.AllChecksPassed && result.BrowserPassed.Value && browserStatus == "PASSED"
            ? new M3Outcome(RunStatus.Passed, null)
            : new M3Outcome(RunStatus.Failed, null);
    }
}
