using System.Diagnostics;
using QaAgent.Application.Abstractions;
using QaAgent.Domain;

namespace QaAgent.Infrastructure.Git;

public sealed class GitCliWorkspace(GitWorkspaceOptions options) : IGitWorkspace
{
    public async Task<GitSnapshot> PrepareAsync(
        string repositoryId,
        CommitSha baseSha,
        CommitSha headSha,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryId);
        Directory.CreateDirectory(options.WorkspaceRoot);
        string workspace = Path.Combine(options.WorkspaceRoot, $"{headSha.Value[..12]}-{Guid.NewGuid():N}");
        try
        {
            await GitAsync(null, ["clone", "--no-checkout", "--", options.Source, workspace], cancellationToken);
            await GitAsync(workspace, ["cat-file", "-e", $"{baseSha.Value}^{{commit}}"], cancellationToken);
            await GitAsync(workspace, ["cat-file", "-e", $"{headSha.Value}^{{commit}}"], cancellationToken);
            await GitAsync(workspace, ["checkout", "--detach", headSha.Value], cancellationToken);

            string names = await GitAsync(workspace,
                ["diff", "--name-status", "--no-renames", "-z", baseSha.Value, headSha.Value], cancellationToken);
            string[] parts = names.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            List<ChangedFile> files = [];
            for (int index = 0; index + 1 < parts.Length; index += 2)
            {
                files.Add(new ChangedFile(parts[index + 1], parts[index]));
            }

            string diff = await GitAsync(workspace,
                ["diff", "--no-ext-diff", "--no-renames", "--binary", baseSha.Value, headSha.Value], cancellationToken);
            return new GitSnapshot(workspace, baseSha, headSha, files, diff);
        }
        catch
        {
            try
            {
                if (Directory.Exists(workspace))
                {
                    Directory.Delete(workspace, recursive: true);
                }
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
            }

            throw;
        }
    }

    public async Task ResetAsync(GitSnapshot snapshot, CancellationToken cancellationToken)
    {
        await GitAsync(snapshot.WorkspacePath, ["checkout", "--force", "--detach", snapshot.HeadSha.Value], cancellationToken);
        await GitAsync(snapshot.WorkspacePath, ["clean", "-fdxq"], cancellationToken);
    }

    private static async Task<string> GitAsync(
        string? directory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo start = new("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        if (directory is not null)
        {
            start.ArgumentList.Add("-C");
            start.ArgumentList.Add(directory);
        }

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start Git.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }

        string stdout = await output;
        string stderr = (await error).Trim();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Git {arguments[0]} failed with exit code {process.ExitCode}: {stderr[..Math.Min(stderr.Length, 300)]}");
        }

        return stdout;
    }
}
