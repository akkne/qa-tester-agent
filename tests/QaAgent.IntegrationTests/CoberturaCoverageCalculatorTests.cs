using QaAgent.Application.Abstractions;
using QaAgent.Infrastructure.Analysis;
using QaAgent.Domain;

namespace QaAgent.IntegrationTests;

public sealed class CoberturaCoverageCalculatorTests
{
    [Fact]
    public async Task CountsExecutableProductionLinesAndAddedLines()
    {
        string workspace = NewWorkspace();
        try
        {
            string report = await WriteReportAsync(workspace, """
                <coverage><packages><package><classes>
                  <class filename="src/Product/Thing.cs"><lines>
                    <line number="2" hits="3" /><line number="3" hits="0" />
                    <line number="8" hits="1" />
                  </lines></class>
                  <class filename="tests/Product.Tests/ThingTests.cs"><lines>
                    <line number="5" hits="1" />
                  </lines></class>
                </classes></package></packages></coverage>
                """);
            GitSnapshot snapshot = Snapshot(workspace, """
                diff --git a/src/Product/Thing.cs b/src/Product/Thing.cs
                --- a/src/Product/Thing.cs
                +++ b/src/Product/Thing.cs
                @@ -1,1 +1,3 @@
                 namespace Product;
                +Run();
                +Fail();
                """);

            var result = await new CoberturaCoverageCalculator().CalculateAsync(snapshot, [report], CancellationToken.None);

            Assert.Equal(3, result.WholeProject.ExecutableLines);
            Assert.Equal(2, result.WholeProject.CoveredLines);
            Assert.Equal(2, result.ChangedLines.ExecutableLines);
            Assert.Equal(1, result.ChangedLines.CoveredLines);
            Assert.Equal(50m, result.ChangedLines.Percent);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task MissingMappingIsUnmeasuredEvenWhenWholeProjectIsMeasured()
    {
        string workspace = NewWorkspace();
        try
        {
            string report = await WriteReportAsync(workspace, """
                <coverage><packages><package><classes>
                  <class filename="src/Product/Other.cs"><lines><line number="2" hits="1" /></lines></class>
                </classes></package></packages></coverage>
                """);
            GitSnapshot snapshot = Snapshot(workspace, """
                diff --git a/src/Product/Thing.cs b/src/Product/Thing.cs
                --- a/src/Product/Thing.cs
                +++ b/src/Product/Thing.cs
                @@ -0,0 +1 @@
                +Run();
                """);

            var result = await new CoberturaCoverageCalculator().CalculateAsync(snapshot, [report], CancellationToken.None);

            Assert.True(result.WholeProject.IsMeasured);
            Assert.False(result.ChangedLines.IsMeasured);
            Assert.Null(result.ChangedLines.Percent);
            Assert.Contains("file mapping", result.ChangedLines.Reason);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task MissingExecutableLineMappingIsUnmeasured()
    {
        string workspace = NewWorkspace();
        try
        {
            string report = await WriteReportAsync(workspace, """
                <coverage><packages><package><classes>
                  <class filename="src/Product/Thing.cs"><lines><line number="2" hits="1" /></lines></class>
                </classes></package></packages></coverage>
                """);
            GitSnapshot snapshot = Snapshot(workspace, """
                diff --git a/src/Product/Thing.cs b/src/Product/Thing.cs
                --- a/src/Product/Thing.cs
                +++ b/src/Product/Thing.cs
                @@ -0,0 +1,2 @@
                +Run();
                +Again();
                """);

            var result = await new CoberturaCoverageCalculator().CalculateAsync(snapshot, [report], CancellationToken.None);

            Assert.False(result.ChangedLines.IsMeasured);
            Assert.Contains("line mapping", result.ChangedLines.Reason);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task ExcludesCommentsAndMovedLines()
    {
        string workspace = NewWorkspace();
        try
        {
            string report = await WriteReportAsync(workspace, """
                <coverage><packages><package><classes>
                  <class filename="src/Product/Thing.cs"><lines>
                    <line number="4" hits="0" /><line number="5" hits="2" />
                  </lines></class>
                </classes></package></packages></coverage>
                """);
            GitSnapshot snapshot = Snapshot(workspace, """
                diff --git a/src/Product/Thing.cs b/src/Product/Thing.cs
                --- a/src/Product/Thing.cs
                +++ b/src/Product/Thing.cs
                @@ -1,2 +1,5 @@
                -Moved();
                 Context();
                +// explanation
                +
                +Moved();
                +New();
                """);

            var result = await new CoberturaCoverageCalculator().CalculateAsync(snapshot, [report], CancellationToken.None);

            Assert.Equal(1, result.ChangedLines.ExecutableLines);
            Assert.Equal(1, result.ChangedLines.CoveredLines);
            Assert.Equal(100m, result.ChangedLines.Percent);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task MissingReportDoesNotBecomeZeroPercent()
    {
        GitSnapshot snapshot = Snapshot("/tmp/unused", string.Empty);

        var result = await new CoberturaCoverageCalculator().CalculateAsync(snapshot, [], CancellationToken.None);

        Assert.Null(result.WholeProject.Percent);
        Assert.Null(result.ChangedLines.Percent);
        Assert.Contains("missing", result.WholeProject.Reason);
    }

    private static string NewWorkspace()
    {
        string path = Path.Combine(Path.GetTempPath(), "qa-coverage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<string> WriteReportAsync(string workspace, string xml)
    {
        string path = Path.Combine(workspace, "coverage.cobertura.xml");
        await File.WriteAllTextAsync(path, xml);
        return path;
    }

    private static GitSnapshot Snapshot(string workspace, string diff) =>
        new(workspace, CommitSha.Parse(new string('a', 40)), CommitSha.Parse(new string('b', 40)), [], diff);
}
