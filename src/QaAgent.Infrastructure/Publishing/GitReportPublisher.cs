using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using QaAgent.Application.Abstractions;
using QaAgent.Application.Analysis;
using QaAgent.Application.Execution;
using QaAgent.Application.Publishing;
using QaAgent.Domain;

namespace QaAgent.Infrastructure.Publishing;

public sealed class GitReportPublisher(
    string artifactRoot,
    string remote,
    string? githubToken,
    string resultBaseUrl) : IReportPublisher
{
    public async Task<PublishedReport> PublishAsync(Run run, GitSnapshot snapshot,
        ProjectProfile profile, M2Result analysis, M3Result execution, RunStatus status,
        CancellationToken cancellationToken)
    {
        string root = Path.GetFullPath(snapshot.WorkspacePath);
        string artifactsRoot = Path.GetFullPath(artifactRoot);
        string artifacts = Path.GetFullPath(execution.ArtifactDirectory);
        if (!artifacts.StartsWith(artifactsRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            !string.Equals(Path.GetFileName(artifacts), run.Id.ToString("N"), StringComparison.Ordinal))
            throw new InvalidOperationException("Execution artifacts do not belong to this run.");
        Directory.CreateDirectory(artifacts);

        string reportDirectory = profile.ReportDirectory.TrimEnd('/');
        if (Path.IsPathRooted(reportDirectory) || reportDirectory.Split('/').Any(part => part is "" or "." or "..") ||
            !string.Equals(reportDirectory, "revision", StringComparison.Ordinal))
            throw new InvalidOperationException("Report directory is not allowed.");
        string reportPath = reportDirectory + "/commit_" + snapshot.HeadSha.Value[..12] + ".md";
        string localReport = Path.Combine(artifacts, "report.md");
        IReadOnlyList<PublishedArtifact> manifest = ArtifactManifest(artifacts);
        string markdown = MarkdownReportRenderer.Render(run, snapshot, analysis, execution, status, manifest, resultBaseUrl);
        await File.WriteAllTextAsync(localReport, markdown, cancellationToken);
        manifest = ArtifactManifest(artifacts);
        string branch = "qa/" + snapshot.HeadSha.Value[..12] + "-" + run.Id.ToString("N")[..12];
        string? webBase = GitHubWebBase(remote);
        string[] allowed = analysis.Tests.Select(test => test.Path.Replace('\\', '/'))
            .Append(reportPath).Distinct(StringComparer.Ordinal).ToArray();
        try
        {
            string checkoutSha = (await GitAsync(root, ["rev-parse", "HEAD"], cancellationToken)).Trim();
            if (!string.Equals(checkoutSha, snapshot.HeadSha.Value, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Publish workspace differs from headSha.");
            foreach (GeneratedTest test in analysis.Tests)
            {
                string path = test.Path.Replace('\\', '/');
                if (Path.IsPathRooted(path) || path.Split('/').Any(part => part is "" or "." or "..") ||
                    !profile.AllowedTestDirectories.Any(dir => path.StartsWith(dir.TrimEnd('/') + "/", StringComparison.Ordinal)) ||
                    !path.EndsWith(".cs", StringComparison.Ordinal))
                    throw new InvalidOperationException("Generated test path is outside the allowed directories.");
                string fullPath = Path.GetFullPath(Path.Combine(root, path));
                if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                    !File.Exists(fullPath) ||
                    !string.Equals(await File.ReadAllTextAsync(fullPath, cancellationToken), test.Source, StringComparison.Ordinal))
                    throw new InvalidOperationException("Generated test differs from the validated candidate: " + path);
            }
            string existing = await GitAsync(root, ["ls-remote", remote, "refs/heads/" + branch], cancellationToken);
            if (!string.IsNullOrWhiteSpace(existing))
            {
                string existingSha = existing.Split('\t')[0];
                await GitAsync(root, ["fetch", "--no-tags", remote, "refs/heads/" + branch], cancellationToken);
                string[] ancestry = (await GitAsync(root, ["rev-list", "--parents", "-n", "1", "FETCH_HEAD"], cancellationToken))
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                string[] existingPaths = (await GitAsync(root,
                    ["diff-tree", "--no-commit-id", "--name-only", "-r", "FETCH_HEAD"], cancellationToken))
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (ancestry.Length != 2 || ancestry[0] != existingSha || ancestry[1] != snapshot.HeadSha.Value ||
                    existingPaths.Length != allowed.Length ||
                    !existingPaths.ToHashSet(StringComparer.Ordinal).SetEquals(allowed))
                    throw new InvalidOperationException("Existing QA branch does not match this run and headSha.");
                foreach (GeneratedTest test in analysis.Tests)
                {
                    string committed = await GitAsync(root, ["show", "FETCH_HEAD:" + test.Path.Replace('\\', '/')], cancellationToken);
                    if (!string.Equals(committed, test.Source, StringComparison.Ordinal))
                        throw new InvalidOperationException("Existing QA branch contains different candidate tests.");
                }
                return new PublishedReport(branch, reportPath, localReport,
                    webBase is null ? null : webBase + "/tree/" + Uri.EscapeDataString(branch),
                    webBase is null ? null : webBase + "/blob/" + Uri.EscapeDataString(branch) + "/" + reportPath,
                    existingSha, manifest, null);
            }
            await GitAsync(root, ["checkout", "-b", branch], cancellationToken);
            string fullReport = Path.Combine(root, reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullReport)!);
            await File.WriteAllTextAsync(fullReport, markdown, cancellationToken);
            await GitAsync(root, ["add", "--", .. allowed], cancellationToken);
            string staged = await GitAsync(root, ["diff", "--cached", "--name-only", "-z"], cancellationToken);
            string[] actual = staged.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            if (actual.Length != allowed.Length || !actual.ToHashSet(StringComparer.Ordinal).SetEquals(allowed))
                throw new InvalidOperationException("Staged paths differ from the candidate tests and report.");
            await GitAsync(root, ["-c", "user.name=QA Agent", "-c", "user.email=qa-agent@users.noreply.github.com",
                "commit", "-m", "test(qa): publish result for " + snapshot.HeadSha.Value[..12]], cancellationToken);
            string commit = (await GitAsync(root, ["rev-parse", "HEAD"], cancellationToken)).Trim();
            await GitAsync(root, ["push", remote, "HEAD:refs/heads/" + branch], cancellationToken);
            string? branchUrl = webBase is null ? null : webBase + "/tree/" + Uri.EscapeDataString(branch);
            string? reportUrl = webBase is null ? null : webBase + "/blob/" + Uri.EscapeDataString(branch) + "/" + reportPath;
            return new PublishedReport(branch, reportPath, localReport, branchUrl, reportUrl, commit, manifest, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            return new PublishedReport(branch, reportPath, localReport, null, null, null, manifest,
                "QA branch publication failed: " + error.Message);
        }
    }

    private static IReadOnlyList<PublishedArtifact> ArtifactManifest(string directory)
    {
        if (!Directory.Exists(directory)) return [];
        List<PublishedArtifact> artifacts = [];
        IEnumerable<string> files = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly);
        foreach (string subdirectory in new[] { "browser", "test-results" })
        {
            string path = Path.Combine(directory, subdirectory);
            if (Directory.Exists(path))
                files = files.Concat(Directory.EnumerateFiles(path, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                }));
        }
        foreach (string file in files)
        {
            string relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
            if (!(relative.EndsWith(".log", StringComparison.OrdinalIgnoreCase) && !relative.Contains('/')) &&
                relative != "report.md" &&
                !(relative.StartsWith("browser/", StringComparison.Ordinal) &&
                    (relative.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                     relative.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                     relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase))) &&
                !(relative.StartsWith("test-results/", StringComparison.Ordinal) &&
                    (relative.EndsWith(".trx", StringComparison.OrdinalIgnoreCase) ||
                     relative.EndsWith("coverage.cobertura.xml", StringComparison.OrdinalIgnoreCase))))
                continue;
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(relative));
            artifacts.Add(new PublishedArtifact(Convert.ToHexString(hash)[..24].ToLowerInvariant(),
                relative, relative, new FileInfo(file).Length));
        }
        return artifacts.OrderBy(item => item.RelativePath, StringComparer.Ordinal).ToArray();
    }

    private static string? GitHubWebBase(string remote)
    {
        if (!Uri.TryCreate(remote, UriKind.Absolute, out Uri? uri) ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return null;
        string path = uri.AbsolutePath.Trim('/');
        if (path.EndsWith(".git", StringComparison.Ordinal)) path = path[..^4];
        return "https://github.com/" + path;
    }

    private async Task<string> GitAsync(string directory, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        ProcessStartInfo start = new("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        string? askpass = null;
        if (!string.IsNullOrWhiteSpace(githubToken) && remote.StartsWith("https://github.com/", StringComparison.Ordinal))
        {
            askpass = Path.Combine(Path.GetTempPath(), "qa-agent-askpass-" + Guid.NewGuid().ToString("N") + ".sh");
            await File.WriteAllTextAsync(askpass,
                "#!/bin/sh\ncase \"$1\" in *Username*) printf 'x-access-token' ;; *Password*) printf '%s' \"$QA_AGENT_GITHUB_TOKEN\" ;; esac\n", cancellationToken);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(askpass, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            start.Environment["GIT_ASKPASS"] = askpass;
            start.Environment["QA_AGENT_GITHUB_TOKEN"] = githubToken;
        }
        foreach (string arg in args) start.ArgumentList.Add(arg);
        try
        {
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Git.");
            Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
            try { await process.WaitForExitAsync(cancellationToken); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw;
            }
            string stdout = await output;
            string stderr = await error;
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Git {args[0]} failed (exit {process.ExitCode}): {stderr[..Math.Min(stderr.Length, 500)]}");
            return stdout;
        }
        finally
        {
            if (askpass is not null) File.Delete(askpass);
        }
    }
}
