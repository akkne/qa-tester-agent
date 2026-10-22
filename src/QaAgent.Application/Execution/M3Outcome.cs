using System.Text.Json;
using QaAgent.Domain;

namespace QaAgent.Application.Execution;

public sealed record M3Outcome(RunStatus Status, string? Reason)
{
    private static readonly string[] RequiredChecks = ["restore", "format-whitespace", "format-style", "build", "test"];

    public static M3Outcome From(M3Result result)
    {
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
