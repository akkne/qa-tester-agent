using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QaAgent.Application;
using QaAgent.Application.Abstractions;
using QaAgent.Application.Analysis;
using QaAgent.Application.Execution;
using QaAgent.Domain;
using QaAgent.Infrastructure.Llm;
using QaAgent.Infrastructure.Persistence;

namespace QaAgent.Host.Api;

public sealed record CreateRunRequest(
    string? Repository,
    string? Branch,
    string? BaseSha,
    string? HeadSha,
    string? ProfileId,
    bool Rerun = false);

public static class RunEndpoints
{
    public static IEndpointRouteBuilder MapRunEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/runs", CreateRunAsync);
        endpoints.MapGet("/api/runs/{id:guid}", GetRunAsync);
        endpoints.MapGet("/api/runs/{id:guid}/report", GetReportAsync);
        endpoints.MapGet("/runs/{id:guid}", GetRunPageAsync);
        endpoints.MapGet("/runs/{id:guid}/activity", GetActivityAsync);
        endpoints.MapGet("/runs/{id:guid}/artifacts/{artifactId}", GetArtifactAsync);
        return endpoints;
    }

    private static async Task<IResult> CreateRunAsync(
        CreateRunRequest request,
        HttpContext context,
        IConfiguration configuration,
        IUnitOfWork unitOfWork,
        RunFactory factory,
        CancellationToken cancellationToken)
    {
        if (!Authorized(context, configuration))
        {
            return Results.Unauthorized();
        }

        string? allowedRepository = configuration["GitHub:Repository"];
        string? allowedBranch = configuration["GitHub:Branch"];
        if (string.IsNullOrWhiteSpace(allowedRepository) ||
            string.IsNullOrWhiteSpace(allowedBranch))
        {
            return Results.Problem("GitHub repository and branch are not configured.", statusCode: 503);
        }

        if (!string.Equals(request.Repository, allowedRepository, StringComparison.Ordinal) ||
            !string.Equals(request.Branch, allowedBranch, StringComparison.Ordinal))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (string.IsNullOrWhiteSpace(request.ProfileId) ||
            !string.Equals(request.ProfileId, "demo", StringComparison.Ordinal))
        {
            return Results.BadRequest(new { error = "Unknown profileId." });
        }

        int profileVersion = configuration.GetValue<int>($"Profiles:{request.ProfileId}:ProfileVersion");
        if (profileVersion <= 0)
        {
            return Results.Problem("Profile version is not configured.", statusCode: 503);
        }

        CommitSha baseSha;
        CommitSha headSha;
        try
        {
            baseSha = CommitSha.Parse(request.BaseSha!);
            headSha = CommitSha.Parse(request.HeadSha!);
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new { error = "baseSha and headSha must be full 40-character commit SHAs." });
        }

        if (baseSha == headSha)
        {
            return Results.BadRequest(new { error = "baseSha and headSha must differ." });
        }

        if (baseSha.Value.All(character => character == '0') ||
            headSha.Value.All(character => character == '0'))
        {
            return Results.BadRequest(new { error = "Real baseSha and headSha commits are required." });
        }

        Run? existing = await unitOfWork.Runs.FindByPushAsync(
            allowedRepository, baseSha, headSha, profileVersion, cancellationToken);
        // A blocked run says nothing about the code, so the next push or a manual rerun gets a fresh attempt.
        if (existing is not null &&
            !(existing.State == RunState.Completed &&
                (existing.FinalStatus == RunStatus.Blocked || request.Rerun)))
        {
            return Accepted(existing, true);
        }

        Run run = factory.Create(Guid.NewGuid(), allowedRepository, baseSha, headSha, profileVersion,
            (existing?.Attempt ?? 0) + 1);
        await unitOfWork.Runs.AddAsync(run, cancellationToken);
        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            existing = await unitOfWork.Runs.FindByPushAsync(
                allowedRepository, baseSha, headSha, profileVersion, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            return Accepted(existing, true);
        }

        return Accepted(run, false);
    }

    private static async Task<IResult> GetRunAsync(
        Guid id,
        HttpContext context,
        IConfiguration configuration,
        IUnitOfWork unitOfWork,
        RunDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!Authorized(context, configuration))
        {
            return Results.Unauthorized();
        }

        Run? run = await unitOfWork.Runs.FindAsync(id, cancellationToken);
        if (run is null)
        {
            return Results.NotFound();
        }

        var stored = await dbContext.Runs.Where(record => record.Id == id)
            .Select(record => new { record.ChangedFilesJson, record.M2ResultJson, record.M3ResultJson, record.M4ResultJson }).SingleAsync(cancellationToken);
        IReadOnlyList<ChangedFile>? changedFiles = stored.ChangedFilesJson is null
            ? null
            : JsonSerializer.Deserialize<List<ChangedFile>>(stored.ChangedFilesJson);
        M2Result? analysis = stored.M2ResultJson is null
            ? null
            : JsonSerializer.Deserialize<M2Result>(stored.M2ResultJson);
        M3Result? execution = stored.M3ResultJson is null
            ? null
            : JsonSerializer.Deserialize<M3Result>(stored.M3ResultJson);
        PublishedReport? publication = stored.M4ResultJson is null
            ? null
            : JsonSerializer.Deserialize<PublishedReport>(stored.M4ResultJson);
        return Results.Ok(new
        {
            run.Id,
            PageUrl = $"/runs/{run.Id}",
            run.RepositoryId,
            BaseSha = run.BaseSha.Value,
            HeadSha = run.HeadSha.Value,
            run.ProfileVersion,
            run.Attempt,
            State = run.State.ToString(),
            FinalStatus = run.FinalStatus?.ToString(),
            run.FailureReason,
            run.CreatedAt,
            run.StartedAt,
            run.FinishedAt,
            ChangedFiles = changedFiles,
            Analysis = analysis is null ? null : new
            {
                analysis.RequirementIds,
                analysis.Plan,
                analysis.Findings,
                Tests = analysis.Tests.Select(test => new { test.Path, test.RequirementIds, test.Kind }),
                analysis.ContextGaps,
                analysis.InputTokens,
                analysis.OutputTokens,
                analysis.GenerationFixes,
                analysis.TestsCompile,
                analysis.BuildDiagnostics,
                analysis.SecretFindings
            },
            Execution = execution,
            Publication = publication
        });
    }

    private static async Task<IResult> GetReportAsync(
        Guid id,
        HttpContext context,
        IConfiguration configuration,
        RunDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!Authorized(context, configuration))
        {
            return Results.Unauthorized();
        }

        string? json = await dbContext.Runs.AsNoTracking().Where(run => run.Id == id)
            .Select(run => run.M4ResultJson).SingleOrDefaultAsync(cancellationToken);
        PublishedReport? publication = json is null ? null : JsonSerializer.Deserialize<PublishedReport>(json);
        if (publication is null)
        {
            return Results.NotFound();
        }

        string configuredRoot = Path.GetFullPath(configuration["Agent:ArtifactRoot"] ?? "artifacts");
        string file = Path.GetFullPath(Path.Combine(configuredRoot, id.ToString("N"), "report.md"));
        return File.Exists(file)
            ? Results.Text(await File.ReadAllTextAsync(file, cancellationToken), "text/markdown; charset=utf-8")
            : Results.NotFound();
    }

    private static async Task<IResult> GetRunPageAsync(Guid id, HttpContext context,
        IConfiguration configuration, RunDbContext db, CancellationToken cancellationToken)
    {
        if (!BrowserAuthorized(context, configuration)) return Challenge(context);
        RunRecord? record = await db.Runs.AsNoTracking().SingleOrDefaultAsync(run => run.Id == id, cancellationToken);
        if (record is null) return Results.NotFound();
        M2Result? analysis = record.M2ResultJson is null ? null : JsonSerializer.Deserialize<M2Result>(record.M2ResultJson);
        M3Result? execution = record.M3ResultJson is null ? null : JsonSerializer.Deserialize<M3Result>(record.M3ResultJson);
        PublishedReport? publication = record.M4ResultJson is null ? null : JsonSerializer.Deserialize<PublishedReport>(record.M4ResultJson);
        static string Escape(string? value) => System.Net.WebUtility.HtmlEncode(value ?? "");
        static bool IsWebUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
        StringBuilder html = new("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>QA result</title><style>body{font:16px system-ui,sans-serif;max-width:880px;margin:3rem auto;padding:0 1rem;color:#1b2633}h1{font-size:1.8rem}section{border-top:1px solid #ccd3db;padding:1rem 0}code{overflow-wrap:anywhere}li{margin:.4rem 0}.status{font-weight:700}a{color:#0759a5}.activity{list-style:none;padding:0}.activity li{border:1px solid #ccd3db;border-radius:8px;padding:.7rem;margin:.6rem 0}.activity small{color:#526579}.activity pre{white-space:pre-wrap;overflow-wrap:anywhere;max-height:28rem;overflow:auto;margin:.5rem 0 0;font:13px ui-monospace,monospace}</style></head><body>");
        html.Append("<h1>QA result</h1><p class=\"status\" id=\"run-status\">Status: ").Append(Escape(record.FinalStatus ?? record.State))
            .Append("</p><p>Run <code>").Append(id).Append("</code> (attempt ").Append(record.Attempt).Append(")</p><p>Range <code>")
            .Append(Escape(record.BaseSha)).Append(".. ").Append(Escape(record.HeadSha)).Append("</code></p>");
        html.Append("<section><h2>Live agent activity</h2><p id=\"activity-state\">Loading activity…</p><ol class=\"activity\" id=\"activity-list\"></ol></section>");
        if (record.FailureReason is not null) html.Append("<p>Reason: ").Append(Escape(record.FailureReason)).Append("</p>");
        if (record.StartedAt is { } started && record.FinishedAt is { } finished)
            html.Append("<p>Duration: ").Append((finished - started).TotalSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)).Append(" seconds</p>");
        if (analysis is not null)
        {
            html.Append("<section><h2>Analysis</h2><p>LLM tokens: ").Append(analysis.InputTokens)
                .Append(" input, ").Append(analysis.OutputTokens).Append(" output</p><h3>Requirements</h3><ul>");
            foreach (string requirement in analysis.RequirementIds) html.Append("<li><code>").Append(Escape(requirement)).Append("</code></li>");
            html.Append("</ul><h3>Candidate tests</h3><ul>");
            foreach (GeneratedTest test in analysis.Tests) html.Append("<li><code>").Append(Escape(test.Path))
                .Append("</code> — ").Append(Escape(test.Kind)).Append("</li>");
            html.Append("</ul><h3>Findings</h3><ul>");
            foreach (AnalysisFinding finding in analysis.Findings) html.Append("<li>").Append(Escape(finding.Status))
                .Append(": ").Append(Escape(finding.Path)).Append(':').Append(finding.Line).Append(" — ")
                .Append(Escape(finding.Observed)).Append("</li>");
            html.Append("</ul>");
            if (analysis.ContextGaps.Count > 0)
            {
                html.Append("<h3>Warnings and context gaps</h3><ul>");
                foreach (string gap in analysis.ContextGaps) html.Append("<li>").Append(Escape(gap)).Append("</li>");
                html.Append("</ul>");
            }
            html.Append("</section>");
        }
        if (execution is not null)
        {
            html.Append("<section><h2>Execution</h2><p>Tests: ").Append(execution.TotalTests?.ToString() ?? "unavailable")
                .Append(" total, ").Append(execution.FailedTests?.ToString() ?? "unavailable").Append(" failed</p><ul>");
            foreach ((string name, StageCheckResult check) in execution.Checks)
                html.Append("<li>").Append(Escape(name)).Append(": ").Append(check.Passed ? "passed" : "failed").Append("</li>");
            html.Append("</ul>");
            if (execution.Agent is { } agent)
            {
                html.Append("<h3>Test suites</h3>");
                if (agent.BlockReason is not null) html.Append("<p>").Append(Escape(agent.BlockReason)).Append("</p>");
                html.Append("<ul>");
                foreach (SuiteResult suite in agent.Suites)
                    html.Append("<li>").Append(Escape(suite.Id)).Append(" (").Append(Escape(suite.Language)).Append('/')
                        .Append(Escape(suite.Framework)).Append("): ").Append(suite.Verdict).Append(", ")
                        .Append(suite.Total?.ToString() ?? "?").Append(" total, ").Append(suite.Failed?.ToString() ?? "?")
                        .Append(" failed").Append(suite.CountsVerified ? "" : " (agent-reported)")
                        .Append(suite.Reason is null ? "" : "; " + Escape(suite.Reason)).Append("</li>");
                foreach (AgentSkippedSuite skipped in agent.NotRun)
                    html.Append("<li>Not run: ").Append(Escape(skipped.Path)).Append(" — ").Append(Escape(skipped.Reason)).Append("</li>");
                foreach (string warning in agent.Warnings)
                    html.Append("<li>Warning: ").Append(Escape(warning)).Append("</li>");
                html.Append("</ul>");
            }
            else
            {
                html.Append("<p>Browser: ").Append(execution.BrowserPassed?.ToString() ?? "not run").Append("</p>");
            }
            if (execution.Coverage is { } coverage)
                html.Append("<p>Changed line coverage: ").Append(coverage.ChangedLines.Percent?.ToString() ?? "unavailable").Append("%</p>");
            html.Append("</section>");
        }
        if (publication is not null)
        {
            html.Append("<section><h2>Publication and evidence</h2>");
            if (IsWebUrl(publication.ReportUrl)) html.Append("<p><a href=\"").Append(Escape(publication.ReportUrl!)).Append("\">GitHub report</a></p>");
            if (IsWebUrl(publication.BranchUrl)) html.Append("<p><a href=\"").Append(Escape(publication.BranchUrl!)).Append("\">QA branch</a></p>");
            if (publication.PublishError is not null) html.Append("<p>").Append(Escape(publication.PublishError)).Append("</p>");
            html.Append("<ul>");
            foreach (PublishedArtifact artifact in publication.Artifacts)
                html.Append("<li><a href=\"/runs/").Append(id).Append("/artifacts/").Append(Escape(artifact.Id))
                    .Append("\">").Append(Escape(artifact.Name)).Append("</a></li>");
            html.Append("</ul></section>");
        }
        html.Append("<script>const initialState=").Append(JsonSerializer.Serialize(record.State)).Append(";let next=0;async function updateActivity(){try{const response=await fetch(location.pathname+'/activity?after='+next,{cache:'no-store'});if(!response.ok)throw new Error('HTTP '+response.status);const data=await response.json();const list=document.getElementById('activity-list');for(const entry of data.entries){const row=document.createElement('li');const label=document.createElement('small');label.textContent=new Date(entry.at).toLocaleTimeString()+' · '+entry.stage+' · '+entry.kind;const body=document.createElement('pre');body.textContent=entry.text;row.append(label,body);list.append(row)}next=data.next;document.getElementById('activity-state').textContent=data.state==='Completed'?'Run finished.':'Updates every 3 seconds.';document.getElementById('run-status').textContent='Status: '+(data.finalStatus||data.state);if(initialState!=='Completed'&&data.state==='Completed'){location.reload();return}}catch(error){document.getElementById('activity-state').textContent='Activity temporarily unavailable: '+error.message}setTimeout(updateActivity,3000)}updateActivity()</script></body></html>");
        return Results.Content(html.ToString(), "text/html; charset=utf-8");
    }

    private static async Task<IResult> GetActivityAsync(Guid id, int? after, HttpContext context,
        IConfiguration configuration, RunDbContext db, ICodexActivityStore activity, CancellationToken cancellationToken)
    {
        if (!BrowserAuthorized(context, configuration)) return Challenge(context);
        var run = await db.Runs.AsNoTracking().Where(record => record.Id == id)
            .Select(record => new { record.State, record.FinalStatus }).SingleOrDefaultAsync(cancellationToken);
        if (run is null) return Results.NotFound();
        IReadOnlyList<CodexActivityEntry> entries = await activity.ReadAsync(id, cancellationToken);
        context.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new
        {
            run.State,
            run.FinalStatus,
            Next = entries.Count,
            Entries = entries.Skip(Math.Clamp(after ?? 0, 0, entries.Count))
        });
    }

    private static async Task<IResult> GetArtifactAsync(Guid id, string artifactId, HttpContext context,
        IConfiguration configuration, RunDbContext db, CancellationToken cancellationToken)
    {
        if (!BrowserAuthorized(context, configuration)) return Challenge(context);
        string? json = await db.Runs.AsNoTracking().Where(run => run.Id == id)
            .Select(run => run.M4ResultJson).SingleOrDefaultAsync(cancellationToken);
        if (json is null) return Results.NotFound();
        PublishedReport? publication = JsonSerializer.Deserialize<PublishedReport>(json);
        PublishedArtifact? artifact = publication?.Artifacts.SingleOrDefault(item => item.Id == artifactId);
        if (artifact is null) return Results.NotFound();
        string configuredRoot = Path.GetFullPath(configuration["Agent:ArtifactRoot"] ?? "artifacts");
        string runRoot = Path.GetFullPath(Path.Combine(configuredRoot, id.ToString("N")));
        string file = Path.GetFullPath(Path.Combine(runRoot, artifact.RelativePath));
        if (!file.StartsWith(runRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(file))
            return Results.NotFound();
        string relative = Path.GetRelativePath(runRoot, file);
        string cursor = runRoot;
        foreach (string part in relative.Split(Path.DirectorySeparatorChar))
        {
            cursor = Path.Combine(cursor, part);
            if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) return Results.NotFound();
        }
        string contentType = Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".log" => "text/plain; charset=utf-8",
            _ => "application/octet-stream"
        };
        return Results.File(file, contentType);
    }

    private static IResult Challenge(HttpContext context)
    {
        context.Response.Headers.WWWAuthenticate = "Basic realm=\"QA Agent\"";
        return Results.Json(new { error = "Use HTTP Basic authentication with the agent API key as password." }, statusCode: 401);
    }

    private static bool BrowserAuthorized(HttpContext context, IConfiguration configuration)
    {
        string header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header[6..]));
            string password = decoded[(decoded.IndexOf(':') + 1)..];
            string? expected = configuration["AgentApi:Key"];
            if (string.IsNullOrEmpty(expected) || decoded.IndexOf(':') < 0) return false;
            byte[] expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
            byte[] suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(password));
            return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
        }
        catch (FormatException) { return false; }
    }

    private static IResult Accepted(Run run, bool duplicate) => Results.Accepted(
        $"/api/runs/{run.Id}",
        new { RunId = run.Id, run.Attempt, State = run.State.ToString(), ResultUrl = $"/api/runs/{run.Id}", PageUrl = $"/runs/{run.Id}", Duplicate = duplicate });

    private static bool Authorized(HttpContext context, IConfiguration configuration)
    {
        string? expected = configuration["AgentApi:Key"];
        string? supplied = context.Request.Headers["X-Agent-Key"].ToString();
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(supplied))
        {
            return false;
        }

        byte[] expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        byte[] suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
    }
}
