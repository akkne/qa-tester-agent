using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using QaAgent.Application.Execution;
using QaAgent.Infrastructure.Analysis;

namespace QaAgent.Infrastructure.Execution;

/// <summary>Reads JUnit XML and TRX reports and the qa-run ledger without trusting the agent's summary.</summary>
public static class TestReportParser
{
    private const long MaxReportBytes = 64L * 1024 * 1024;

    public static ParsedTestReport Parse(string path)
    {
        try
        {
            if (new FileInfo(path).Length > MaxReportBytes)
                return Invalid(path, "report is larger than 64 MB");
            using FileStream stream = File.OpenRead(path);
            using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            });
            XElement root = XDocument.Load(reader).Root ?? throw new FormatException("empty document");
            return root.Name.LocalName switch
            {
                "TestRun" => Trx(path, root),
                "testsuites" or "testsuite" => JUnit(path, root),
                _ => Invalid(path, $"unknown report root <{root.Name.LocalName}>")
            };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or XmlException or FormatException)
        {
            return Invalid(path, error.Message);
        }
    }

    public static IReadOnlyList<CommandRecord> ReadLedger(string ledgerPath, string artifactsDirectory, ICollection<string> warnings)
    {
        if (!File.Exists(ledgerPath) || WorkspacePath.HasLink(Path.GetFullPath(artifactsDirectory), Path.GetFullPath(ledgerPath))) return [];
        List<CommandRecord> records = [];
        foreach (string line in File.ReadLines(ledgerPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                string phase = root.GetProperty("phase").GetString() ?? "";
                if (phase is not ("setup" or "build" or "test"))
                    throw new FormatException("unknown phase " + phase);
                string log = root.GetProperty("log").GetString() ?? "";
                records.Add(new CommandRecord(
                    root.GetProperty("seq").GetInt32(),
                    root.GetProperty("suite").GetString() ?? throw new FormatException("missing suite"),
                    phase,
                    root.GetProperty("exitCode").GetInt32(),
                    MapArtifact(artifactsDirectory, log) ?? "",
                    root.GetProperty("command").GetString() ?? ""));
            }
            catch (Exception error) when (error is JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
            {
                warnings.Add("Ignored an invalid qa-run ledger line: " + error.Message);
            }
        }

        return records;
    }

    /// <summary>Maps a container path or a path relative to the artifacts mount to a host file inside the run's artifacts.</summary>
    public static string? MapArtifact(string artifactsDirectory, string path)
    {
        string normalized = path.Replace('\\', '/');
        if (normalized.StartsWith(AgentTestContract.ArtifactsMount + "/", StringComparison.Ordinal))
            normalized = normalized[(AgentTestContract.ArtifactsMount.Length + 1)..];
        if (normalized.Length == 0 || Path.IsPathRooted(normalized)) return null;
        string root = Path.GetFullPath(artifactsDirectory);
        string full = Path.GetFullPath(Path.Combine(root, normalized));
        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !WorkspacePath.HasLink(root, full)
            ? full : null;
    }

    private static ParsedTestReport JUnit(string path, XElement root)
    {
        // Count test cases directly: suite-level attributes are often missing or aggregated twice in nested suites.
        XElement[] cases = root.DescendantsAndSelf().Where(element => element.Name.LocalName == "testcase").ToArray();
        List<string> failed = [];
        int skipped = 0;
        foreach (XElement testCase in cases)
        {
            string[] children = testCase.Elements().Select(element => element.Name.LocalName).ToArray();
            if (children.Contains("failure") || children.Contains("error"))
                failed.Add(Name(testCase.Attribute("classname")?.Value, testCase.Attribute("name")?.Value));
            else if (children.Contains("skipped"))
                skipped++;
        }

        return new ParsedTestReport(path, cases.Length, failed.Count, skipped, failed);
    }

    private static ParsedTestReport Trx(string path, XElement root)
    {
        // Data-driven tests nest one result per row under <InnerResults>; count the rows, not their parent too.
        XElement[] results = root.Descendants().Where(element => element.Name.LocalName == "UnitTestResult" &&
            !element.Descendants().Any(inner => inner.Name.LocalName == "UnitTestResult")).ToArray();
        List<string> failed = [];
        int skipped = 0;
        foreach (XElement result in results)
        {
            string outcome = result.Attribute("outcome")?.Value ?? "";
            if (outcome is "Failed" or "Error" or "Timeout" or "Aborted" or "Disconnected")
                failed.Add(result.Attribute("testName")?.Value ?? "unnamed test");
            else if (outcome is "NotExecuted" or "Inconclusive" or "Pending")
                skipped++;
        }

        return new ParsedTestReport(path, results.Length, failed.Count, skipped, failed);
    }

    private static string Name(string? className, string? name) =>
        string.IsNullOrWhiteSpace(className) ? name ?? "unnamed test" : className + "." + name;

    private static ParsedTestReport Invalid(string path, string error) => new(path, 0, 0, 0, [], error);
}
