using QaAgent.Application.Abstractions;
using QaAgent.Application.Analysis;
using QaAgent.Domain;
using QaAgent.Infrastructure.Analysis;

namespace QaAgent.IntegrationTests;

public sealed class M2ContextAndGuardTests
{
    [Fact]
    public async Task SecretScannerReportsAddedLineWithoutExposingValue()
    {
        string diff = "diff --git a/app.cs b/app.cs\n+++ b/app.cs\n@@ -1,1 +1,2 @@\n unchanged\n+apiKey=abcdefghijklmnop\n";
        IReadOnlyList<SecretFinding> findings = await new DiffSecretScanner().ScanAsync(
            Snapshot("/tmp/workspace", [new ChangedFile("app.cs", "M")], diff), CancellationToken.None);

        SecretFinding finding = Assert.Single(findings);
        Assert.Equal("app.cs", finding.Path);
        Assert.Equal(2, finding.Line);
        Assert.Equal("secret assignment", finding.Kind);
    }

    [Fact]
    public async Task SecretScannerDoesNotTreatTestVariablesAsHardCodedSecrets()
    {
        string diff = "diff --git a/demo/Demo.Tests/Test.cs b/demo/Demo.Tests/Test.cs\n" +
            "+++ b/demo/Demo.Tests/Test.cs\n@@ -0,0 +1,2 @@\n" +
            "+string password = oneNonWhitespace ? new string(' ', 11) + \"Z\" : new string('Z', 12);\n" +
            "+Match token = Regex.Match(page, \"name=\\\"token\\\"\");\n";

        IReadOnlyList<SecretFinding> findings = await new DiffSecretScanner().ScanAsync(
            Snapshot("/tmp/workspace", [new ChangedFile("demo/Demo.Tests/Test.cs", "A")], diff), CancellationToken.None);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task ContextContainsChangedSourceButNotUnchangedSourceAndRedactsAssignment()
    {
        string root = Path.Combine(Path.GetTempPath(), "qa-m2-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "demo/docs/specs"));
            Directory.CreateDirectory(Path.Combine(root, "demo/Demo.Web"));
            await File.WriteAllTextAsync(Path.Combine(root, "demo/docs/specs/feature.md"),
                "| DEMO-REG-01 | Create account |\n");
            await File.WriteAllTextAsync(Path.Combine(root, "demo/Demo.Web/Changed.cs"),
                "class Changed { string marker = \"changed-marker\"; string apiKey=secret123; }");
            await File.WriteAllTextAsync(Path.Combine(root, "demo/Demo.Web/Unchanged.cs"),
                "class Unchanged { string marker = \"unchanged-marker\"; }");

            M2Context context = await new M2ContextReader().ReadAsync(
                Snapshot(root, [new ChangedFile("demo/Demo.Web/Changed.cs", "M")], "diff --git\n+token=secret123"),
                Profile(), CancellationToken.None);

            Assert.Contains("DEMO-REG-01", context.RequirementIds);
            Assert.Contains("changed-marker", context.Text);
            Assert.DoesNotContain("unchanged-marker", context.Text);
            Assert.DoesNotContain("secret123", context.Text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ContextIncludesChangedTestBeforeLargeUnchangedTestFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "qa-m2-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "demo/docs/specs"));
            Directory.CreateDirectory(Path.Combine(root, "demo/Demo.Tests/Admin"));
            Directory.CreateDirectory(Path.Combine(root, "demo/Demo.Tests/Host"));
            await File.WriteAllTextAsync(Path.Combine(root, "demo/docs/specs/feature.md"),
                "| DEMO-REG-01 | Create account |\n");
            await File.WriteAllTextAsync(Path.Combine(root, "demo/Demo.Tests/Admin/Unchanged.cs"),
                new string('x', 40_000));
            await File.WriteAllTextAsync(Path.Combine(root, "demo/Demo.Tests/Host/CompositionTests.cs"),
                "class CompositionTests { /* changed-test-marker */ }");

            M2Context context = await new M2ContextReader().ReadAsync(
                Snapshot(root, [new ChangedFile("demo/Demo.Tests/Host/CompositionTests.cs", "M")], "diff"),
                Profile(), CancellationToken.None);

            Assert.Contains("changed-test-marker", context.Text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SecretScannerIgnoresNoNewlineMarkerForLineNumbers()
    {
        string diff = "diff --git a/app.cs b/app.cs\n+++ b/app.cs\n@@ -1,2 +1,3 @@\n-old\n\\ No newline at end of file\n+new\n+apiKey=abcdefghijklmnop\n";
        IReadOnlyList<SecretFinding> findings = await new DiffSecretScanner().ScanAsync(
            Snapshot("/tmp/workspace", [new ChangedFile("app.cs", "M")], diff), CancellationToken.None);

        Assert.Equal(2, Assert.Single(findings).Line);
    }

    [Fact]
    public async Task ChangedRequirementIdsComeOnlyFromSpecificationFilesAndKnownIds()
    {
        string root = Path.Combine(Path.GetTempPath(), "qa-m2-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "demo/docs/specs"));
            await File.WriteAllTextAsync(Path.Combine(root, "demo/docs/specs/feature.md"),
                "| DEMO-REG-01 | Create account |\n");
            string diff = "diff --git a/demo/docs/specs/feature.md b/demo/docs/specs/feature.md\n+++ b/demo/docs/specs/feature.md\n" +
                "@@ -0,0 +1,2 @@\n+| DEMO-REG-01 | Create account |\n+| OTHER-99 | Unknown |\n" +
                "diff --git a/docs/other.md b/docs/other.md\n+++ b/docs/other.md\n@@ -0,0 +1,1 @@\n+| DEMO-REG-02 | Elsewhere |\n";

            M2Context context = await new M2ContextReader().ReadAsync(
                Snapshot(root, [new ChangedFile("demo/docs/specs/feature.md", "M")], diff), Profile(), CancellationToken.None);

            Assert.Equal(["DEMO-REG-01"], context.ChangedRequirementIds);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SymlinkedSourceFileIsNotReadIntoContext()
    {
        string root = Path.Combine(Path.GetTempPath(), "qa-m2-" + Guid.NewGuid().ToString("N"));
        string outside = Path.Combine(Path.GetTempPath(), "qa-m2-out-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "demo/docs/specs"));
            Directory.CreateDirectory(Path.Combine(root, "demo/Demo.Web"));
            Directory.CreateDirectory(outside);
            await File.WriteAllTextAsync(Path.Combine(root, "demo/docs/specs/feature.md"), "| DEMO-REG-01 | x |\n");
            await File.WriteAllTextAsync(Path.Combine(outside, "Secret.cs"), "class S { /* outside-marker */ }");
            File.CreateSymbolicLink(Path.Combine(root, "demo/Demo.Web/Link.cs"), Path.Combine(outside, "Secret.cs"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => new M2ContextReader().ReadAsync(
                Snapshot(root, [new ChangedFile("demo/Demo.Web/Link.cs", "A")], "diff"), Profile(), CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

    private static ProjectProfile Profile() =>
        new("demo", 2, "demo/docs/specs", ["demo/Demo.Tests"], "revision");

    private static GitSnapshot Snapshot(string root, IReadOnlyList<ChangedFile> files, string diff) =>
        new(root, CommitSha.Parse(new string('a', 40)), CommitSha.Parse(new string('b', 40)), files, diff);
}
