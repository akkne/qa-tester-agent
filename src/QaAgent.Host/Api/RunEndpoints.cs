using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QaAgent.Application;
using QaAgent.Application.Abstractions;
using QaAgent.Domain;
using QaAgent.Infrastructure.Persistence;

namespace QaAgent.Host.Api;

public sealed record CreateRunRequest(
    string? Repository,
    string? Branch,
    string? BaseSha,
    string? HeadSha,
    string? ProfileId);

public static class RunEndpoints
{
    public static IEndpointRouteBuilder MapRunEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/runs", CreateRunAsync);
        endpoints.MapGet("/api/runs/{id:guid}", GetRunAsync);
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

        if (baseSha.Value.All(character => character == '0'))
        {
            return Results.BadRequest(new { error = "A real first baseSha is required." });
        }

        Run? existing = await unitOfWork.Runs.FindByPushAsync(
            allowedRepository, baseSha, headSha, profileVersion, cancellationToken);
        if (existing is not null)
        {
            return Accepted(existing, true);
        }

        Run run = factory.Create(Guid.NewGuid(), allowedRepository, baseSha, headSha, profileVersion);
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

        string? filesJson = await dbContext.Runs.Where(record => record.Id == id)
            .Select(record => record.ChangedFilesJson).SingleAsync(cancellationToken);
        IReadOnlyList<ChangedFile>? changedFiles = filesJson is null
            ? null
            : JsonSerializer.Deserialize<List<ChangedFile>>(filesJson);
        return Results.Ok(new
        {
            run.Id,
            run.RepositoryId,
            BaseSha = run.BaseSha.Value,
            HeadSha = run.HeadSha.Value,
            run.ProfileVersion,
            State = run.State.ToString(),
            FinalStatus = run.FinalStatus?.ToString(),
            run.FailureReason,
            run.CreatedAt,
            run.StartedAt,
            run.FinishedAt,
            ChangedFiles = changedFiles
        });
    }

    private static IResult Accepted(Run run, bool duplicate) => Results.Accepted(
        $"/api/runs/{run.Id}",
        new { RunId = run.Id, State = run.State.ToString(), ResultUrl = $"/api/runs/{run.Id}", Duplicate = duplicate });

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
