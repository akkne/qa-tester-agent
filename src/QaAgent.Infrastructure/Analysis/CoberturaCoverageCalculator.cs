using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using QaAgent.Application.Abstractions;
using QaAgent.Application.Analysis;

namespace QaAgent.Infrastructure.Analysis;

public sealed partial class CoberturaCoverageCalculator : ICoverageCalculator
{
    public async Task<CoverageResult> CalculateAsync(
        GitSnapshot snapshot,
        IReadOnlyList<string> coberturaPaths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(coberturaPaths);

        if (coberturaPaths.Count == 0)
        {
            return Unmeasured("Cobertura report is missing.");
        }

        Dictionary<string, Dictionary<int, int>> coverage = new(StringComparer.Ordinal);
        foreach (string reportPath in coberturaPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await ReadReportAsync(snapshot.WorkspacePath, reportPath, coverage, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or XmlException or UnauthorizedAccessException or FormatException)
            {
                return Unmeasured($"Cobertura report could not be read: {Path.GetFileName(reportPath)}.");
            }
        }

        int wholeExecutable = coverage.Values.Sum(lines => lines.Count);
        int wholeCovered = coverage.Values.Sum(lines => lines.Values.Count(hits => hits > 0));
        CoverageMetric whole = wholeExecutable == 0
            ? new CoverageMetric(0, 0, "Cobertura contains no mapped production lines.")
            : new CoverageMetric(wholeCovered, wholeExecutable);

        CoverageMetric changed = CalculateChanged(snapshot, coverage);
        return new CoverageResult(whole, changed);
    }

    private static async Task ReadReportAsync(
        string workspacePath,
        string reportPath,
        Dictionary<string, Dictionary<int, int>> coverage,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(reportPath);
        using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        });
        XDocument document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
        if (document.Root?.Name.LocalName != "coverage")
        {
            throw new FormatException("Expected Cobertura coverage root.");
        }

        foreach (XElement classElement in document.Descendants().Where(element => element.Name.LocalName == "class"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? filename = classElement.Attribute("filename")?.Value;
            if (string.IsNullOrWhiteSpace(filename))
            {
                continue;
            }

            string path = NormalizeCoveragePath(workspacePath, filename);
            if (!IsProductionSource(path))
            {
                continue;
            }

            if (!coverage.TryGetValue(path, out Dictionary<int, int>? lines))
            {
                lines = new Dictionary<int, int>();
                coverage.Add(path, lines);
            }

            foreach (XElement line in classElement.Descendants().Where(element => element.Name.LocalName == "line"))
            {
                if (!int.TryParse(line.Attribute("number")?.Value, NumberStyles.None,
                        CultureInfo.InvariantCulture, out int number) || number <= 0 ||
                    !int.TryParse(line.Attribute("hits")?.Value, NumberStyles.None,
                        CultureInfo.InvariantCulture, out int hits) || hits < 0)
                {
                    throw new FormatException("Invalid Cobertura line.");
                }

                lines[number] = Math.Max(lines.GetValueOrDefault(number), hits);
            }
        }
    }

    private static CoverageMetric CalculateChanged(
        GitSnapshot snapshot,
        IReadOnlyDictionary<string, Dictionary<int, int>> coverage)
    {
        IReadOnlyList<AddedLine> additions = ParseAddedLines(snapshot.Diff);
        if (additions.Count == 0)
        {
            return new CoverageMetric(0, 0, "No changed production lines to measure.");
        }

        Dictionary<string, Dictionary<int, int>> resolved = new(StringComparer.Ordinal);
        foreach (string path in additions.Select(line => line.Path).Distinct(StringComparer.Ordinal))
        {
            string[] matches = coverage.Keys.Where(candidate => PathMatches(candidate, path)).ToArray();
            if (matches.Length != 1)
            {
                return new CoverageMetric(0, 0,
                    matches.Length == 0
                        ? $"Cobertura has no file mapping for {path}."
                        : $"Cobertura file mapping is ambiguous for {path}.");
            }

            resolved.Add(path, coverage[matches[0]]);
        }

        int executable = 0;
        int covered = 0;
        foreach (AddedLine line in additions)
        {
            if (resolved[line.Path].TryGetValue(line.Number, out int hits))
            {
                executable++;
                if (hits > 0)
                {
                    covered++;
                }
            }
            else if (!IsDefinitelyNonExecutable(line.Text))
            {
                return new CoverageMetric(0, 0,
                    $"Cobertura has no line mapping for {line.Path}:{line.Number}.");
            }
        }

        return executable == 0
            ? new CoverageMetric(0, 0, "No executable changed production lines were measured.")
            : new CoverageMetric(covered, executable);
    }

    private static IReadOnlyList<AddedLine> ParseAddedLines(string diff)
    {
        List<AddedLine> added = [];
        Dictionary<string, int> removed = new(StringComparer.Ordinal);
        string? path = null;
        string? oldPath = null;
        int newLine = 0;
        bool inHunk = false;
        foreach (string raw in diff.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                path = null;
                oldPath = null;
                inHunk = false;
            }
            else if (line.StartsWith("--- a/", StringComparison.Ordinal))
            {
                oldPath = NormalizePath(line[6..]);
            }
            else if (line.StartsWith("+++ b/", StringComparison.Ordinal))
            {
                path = NormalizePath(line[6..]);
            }
            else if (line.StartsWith("+++ /dev/null", StringComparison.Ordinal))
            {
                path = null;
            }
            else if (HunkPattern().Match(line) is { Success: true } hunk)
            {
                newLine = int.Parse(hunk.Groups[1].Value, CultureInfo.InvariantCulture);
                inHunk = true;
            }
            else if (inHunk && line.StartsWith('+') && !line.StartsWith("+++", StringComparison.Ordinal))
            {
                if (path is not null && IsProductionSource(path))
                {
                    added.Add(new AddedLine(path, newLine, line[1..]));
                }

                newLine++;
            }
            else if (inHunk && line.StartsWith('-') && !line.StartsWith("---", StringComparison.Ordinal))
            {
                if (oldPath is not null && IsProductionSource(oldPath))
                {
                    string content = line[1..].Trim();
                    removed[content] = removed.GetValueOrDefault(content) + 1;
                }
            }
            else if (inHunk && line.StartsWith(' '))
            {
                newLine++;
            }
        }

        // An unchanged line moved within the diff is not a newly changed line.
        return added.Where(line =>
        {
            string content = line.Text.Trim();
            if (removed.GetValueOrDefault(content) == 0)
            {
                return true;
            }

            removed[content]--;
            return false;
        }).ToArray();
    }

    private static string NormalizeCoveragePath(string workspacePath, string filename)
    {
        string normalized = NormalizePath(filename);
        string workspace = NormalizePath(Path.GetFullPath(workspacePath)).TrimEnd('/');
        if (normalized.StartsWith(workspace + '/', StringComparison.Ordinal))
        {
            return normalized[(workspace.Length + 1)..];
        }

        return normalized.TrimStart('/');
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/').TrimStart('.', '/');

    private static bool PathMatches(string coveragePath, string changedPath) =>
        string.Equals(coveragePath, changedPath, StringComparison.Ordinal) ||
        coveragePath.EndsWith('/' + changedPath, StringComparison.Ordinal) ||
        changedPath.EndsWith('/' + coveragePath, StringComparison.Ordinal);

    private static bool IsProductionSource(string path)
    {
        if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return !segments.Any(segment =>
            segment.Equals("test", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("tests", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            segment.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase) ||
            segment.EndsWith(".Test", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDefinitelyNonExecutable(string text)
    {
        string trimmed = text.Trim();
        return trimmed.Length == 0 || trimmed is "{" or "}" or "};" or "*/" ||
            trimmed.StartsWith("//", StringComparison.Ordinal) ||
            trimmed.StartsWith("/*", StringComparison.Ordinal) ||
            trimmed.StartsWith('*') ||
            trimmed.StartsWith('#') ||
            trimmed.StartsWith("using ", StringComparison.Ordinal) ||
            trimmed.StartsWith("namespace ", StringComparison.Ordinal) ||
            trimmed.StartsWith('[') ||
            DeclarationPattern().IsMatch(trimmed);
    }

    private static CoverageResult Unmeasured(string reason) =>
        new(new CoverageMetric(0, 0, reason), new CoverageMetric(0, 0, reason));

    private sealed record AddedLine(string Path, int Number, string Text);

    [GeneratedRegex(@"^@@ -\d+(?:,\d+)? \+(\d+)(?:,\d+)? @@")]
    private static partial Regex HunkPattern();

    [GeneratedRegex(@"^(?:(?:public|private|protected|internal|static|sealed|abstract|partial|readonly|record|class|struct|interface|enum)\s+)+(?:[^=;]+)(?:\{|$)")]
    private static partial Regex DeclarationPattern();
}
