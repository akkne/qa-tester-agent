using System.Diagnostics;
using QaAgent.Application.Abstractions;
using QaAgent.Application.Analysis;
using QaAgent.Application.Execution;
using QaAgent.Domain;
using QaAgent.Infrastructure.Publishing;

namespace QaAgent.IntegrationTests;

public sealed class M4PublishingTests
{
    [Fact]
    public async Task PublishesOnlyGeneratedTestsAndReportFromTheExactHeadCommit()
    {
        string root = Path.Combine(Path.GetTempPath(), "qa-m4-publish-" + Guid.NewGuid().ToString("N"));
        string remote = Path.Combine(root, "remote.git");
        string seed = Path.Combine(root, "seed");
        string workspace = Path.Combine(root, "workspace");
        string artifactRoot = Path.Combine(root, "artifacts");
        Directory.CreateDirectory(root);
        try
        {
            await GitAsync(root, "init", "--bare", "--initial-branch=main", remote);
            await GitAsync(root, "clone", "--quiet", remote, seed);
            await GitAsync(seed, "config", "user.name", "QA Test");
            await GitAsync(seed, "config", "user.email", "qa-test@example.invalid");
            Directory.CreateDirectory(Path.Combine(seed, "demo", "Demo.Web"));
            await File.WriteAllTextAsync(Path.Combine(seed, "demo", "Demo.Web", "Feature.cs"), "class Feature { int Value => 1; }\n");
            await GitAsync(seed, "add", ".");
            await GitAsync(seed, "commit", "--quiet", "-m", "base");
            string baseSha = (await GitAsync(seed, "rev-parse", "HEAD")).Trim();
            await File.WriteAllTextAsync(Path.Combine(seed, "demo", "Demo.Web", "Feature.cs"), "class Feature { int Value => 2; }\n");
            await GitAsync(seed, "add", ".");
            await GitAsync(seed, "commit", "--quiet", "-m", "feature");
            string headSha = (await GitAsync(seed, "rev-parse", "HEAD")).Trim();
            await GitAsync(seed, "push", "--quiet", "origin", "HEAD:main");

            await GitAsync(root, "clone", "--quiet", remote, workspace);
            await GitAsync(workspace, "checkout", "--quiet", "--detach", headSha);
            const string testPath = "demo/Demo.Tests/Generated/FeatureTests.cs";
            const string testSource = "namespace Demo.Tests; public sealed class FeatureTests { }\n";
            string generatedPath = Path.Combine(workspace, "demo", "Demo.Tests", "Generated", "FeatureTests.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(generatedPath)!);
            await File.WriteAllTextAsync(generatedPath, testSource);
            await File.WriteAllTextAsync(Path.Combine(workspace, "demo", "Demo.Web", "Feature.cs"), "class Feature { int Value => 999; }\n");

            Guid runId = Guid.NewGuid();
            Run run = Run.Queue(runId, "owner/repo", CommitSha.Parse(baseSha), CommitSha.Parse(headSha), 2, DateTimeOffset.UtcNow);
            GitSnapshot snapshot = new(workspace, CommitSha.Parse(baseSha), CommitSha.Parse(headSha),
                [new ChangedFile("demo/Demo.Web/Feature.cs", "M")],
                await GitAsync(seed, "diff", baseSha, headSha));
            ProjectProfile profile = new("demo", 2, "demo/docs/specs", ["demo/Demo.Tests"], "revision");
            M2Result analysis = new(["DEMO-01"], [], [], [new GeneratedTest(testPath, ["DEMO-01"], "positive", testSource)],
                [], 23, 11, 0, true, null, []);
            string runArtifacts = Path.Combine(artifactRoot, runId.ToString("N"));
            Directory.CreateDirectory(Path.Combine(runArtifacts, "browser"));
            await File.WriteAllBytesAsync(Path.Combine(runArtifacts, "browser", "failure.png"), [1, 2, 3]);
            M3Result execution = new(headSha, runArtifacts,
                new Dictionary<string, StageCheckResult> { ["test"] = new(true, 0, "", "Passed: 1") },
                1, 0, new CoverageResult(new CoverageMetric(1, 1), new CoverageMetric(1, 1)),
                true, null, true, "{\"status\":\"PASSED\"}");

            GitReportPublisher publisher = new(artifactRoot, remote, null, "http://localhost:5000");
            PublishedReport published = await publisher.PublishAsync(run, snapshot, profile, analysis, execution,
                RunStatus.Passed, CancellationToken.None);

            Assert.Null(published.PublishError);
            Assert.Equal($"revision/commit_{headSha[..12]}.md", published.ReportPath);
            Assert.Contains(runId.ToString("N")[..12], published.Branch, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(published.LocalReportPath));
            Assert.Equal(2, published.Artifacts.Count);
            PublishedArtifact artifact = Assert.Single(published.Artifacts,
                item => item.RelativePath == "browser/failure.png");
            Assert.Equal("browser/failure.png", artifact.RelativePath);
            Assert.Contains(published.Artifacts, item => item.RelativePath == "report.md");
            string branchHead = (await GitAsync(remote, "rev-parse", $"refs/heads/{published.Branch}")).Trim();
            Assert.Equal(headSha, (await GitAsync(remote, "rev-parse", branchHead + "^")).Trim());
            Assert.Equal(testSource, await GitAsync(remote, "show", $"{branchHead}:{testPath}"));
            string publishedSource = await GitAsync(remote, "show", $"{branchHead}:demo/Demo.Web/Feature.cs");
            Assert.Equal("class Feature { int Value => 2; }\n", publishedSource);
            string report = await GitAsync(remote, "show", $"{branchHead}:{published.ReportPath}");
            Assert.Contains(headSha, report);
            Assert.Contains("DEMO-01", report);
            Assert.Contains("23", report);
            Assert.Contains($"/runs/{runId}/artifacts/{artifact.Id}", report);
            Assert.DoesNotContain("999", report);

            string secondWorkspace = Path.Combine(root, "tampered-workspace");
            await GitAsync(root, "clone", "--quiet", remote, secondWorkspace);
            await GitAsync(secondWorkspace, "checkout", "--quiet", "--detach", headSha);
            string tamperedTest = Path.Combine(secondWorkspace, "demo", "Demo.Tests", "Generated", "FeatureTests.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(tamperedTest)!);
            await File.WriteAllTextAsync(tamperedTest, "// changed after validation\n");
            PublishedReport refused = await publisher.PublishAsync(run, snapshot with { WorkspacePath = secondWorkspace },
                profile, analysis, execution, RunStatus.Passed, CancellationToken.None);
            Assert.NotNull(refused.PublishError);
            Assert.True(File.Exists(refused.LocalReportPath));
            Assert.Equal(branchHead, (await GitAsync(remote, "rev-parse", $"refs/heads/{published.Branch}")).Trim());

            string retryWorkspace = Path.Combine(root, "retry-workspace");
            await GitAsync(root, "clone", "--quiet", remote, retryWorkspace);
            await GitAsync(retryWorkspace, "checkout", "--quiet", "--detach", headSha);
            string retryTest = Path.Combine(retryWorkspace, "demo", "Demo.Tests", "Generated", "FeatureTests.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(retryTest)!);
            await File.WriteAllTextAsync(retryTest, testSource);
            PublishedReport retry = await publisher.PublishAsync(run, snapshot with { WorkspacePath = retryWorkspace },
                profile, analysis, execution, RunStatus.Passed, CancellationToken.None);
            Assert.Null(retry.PublishError);
            Assert.Equal(published.Branch, retry.Branch);
            Assert.Equal(branchHead, retry.CommitSha);
            Assert.Equal(branchHead, (await GitAsync(remote, "rev-parse", $"refs/heads/{retry.Branch}")).Trim());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<string> GitAsync(string directory, params string[] arguments)
    {
        ProcessStartInfo start = new("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start Git.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string stdout = await output;
        string stderr = await error;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {arguments[0]} failed: {stderr}\n{stdout}");
        }

        return stdout;
    }
}
