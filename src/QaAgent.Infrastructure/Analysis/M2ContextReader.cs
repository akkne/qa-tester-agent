using System.Text;
using System.Text.RegularExpressions;
using QaAgent.Application.Abstractions;
using QaAgent.Application.Analysis;

namespace QaAgent.Infrastructure.Analysis;

public sealed partial class M2ContextReader : IM2ContextReader
{
    public async Task<M2Context> ReadAsync(
        GitSnapshot snapshot, ProjectProfile profile, CancellationToken cancellationToken)
    {
        string root = Path.GetFullPath(snapshot.WorkspacePath);
        string specRoot = Resolve(root, profile.SpecificationDirectory);
        if (!Directory.Exists(specRoot))
        {
            throw new DirectoryNotFoundException($"Specification directory unavailable: {profile.SpecificationDirectory}.");
        }

        StringBuilder text = new();
        List<string> ids = [];
        List<string> gaps = [];
        foreach (string spec in Directory.EnumerateFiles(specRoot, "*.md", SearchOption.AllDirectories)
            .Where(path => !WorkspacePath.HasLink(root, path)).Order(StringComparer.Ordinal))
        {
            string content = await File.ReadAllTextAsync(spec, cancellationToken);
            foreach (Match match in RequirementIdRegex().Matches(content))
            {
                ids.Add(match.Groups[1].Value);
            }

            Append(text, root, spec, content, 50_000);
        }

        if (ids.Count == 0)
        {
            gaps.Add("No requirement IDs were found in Markdown specifications.");
        }

        text.AppendLine("\n=== Git diff ===");
        text.AppendLine(Redact(snapshot.Diff[..Math.Min(snapshot.Diff.Length, 80_000)]));
        if (snapshot.Diff.Length > 80_000)
        {
            gaps.Add("Diff truncated at 80,000 characters.");
        }

        int sourceBudget = 80_000;
        foreach (ChangedFile file in snapshot.Files)
        {
            if (!file.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                profile.AllowedTestDirectories.Any(directory => Under(file.Path, directory)))
            {
                continue;
            }

            string path = Resolve(root, file.Path);
            if (File.Exists(path))
            {
                string content = await File.ReadAllTextAsync(path, cancellationToken);
                int allowed = Math.Min(content.Length, sourceBudget);
                Append(text, root, path, content, allowed);
                sourceBudget -= allowed;
                if (allowed < content.Length)
                {
                    gaps.Add($"Changed source truncated: {file.Path}.");
                }
            }

            if (sourceBudget == 0)
            {
                gaps.Add("Changed source budget exhausted.");
                break;
            }
        }

        int testBudget = 40_000;
        foreach (string directory in profile.AllowedTestDirectories)
        {
            string testRoot = Resolve(root, directory);
            if (!Directory.Exists(testRoot))
            {
                continue;
            }

            HashSet<string> changedTests = snapshot.Files
                .Where(file => file.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
                    Under(file.Path, directory))
                .Select(file => Resolve(root, file.Path))
                .ToHashSet(StringComparer.Ordinal);
            string[] paths = Directory.EnumerateFiles(testRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !WorkspacePath.HasLink(root, path))
                .OrderBy(path => !changedTests.Contains(path))
                .ThenBy(path => changedTests.Any(changed =>
                    Path.GetDirectoryName(changed) == Path.GetDirectoryName(path)) ? 0 : 1)
                .ThenBy(path => path, StringComparer.Ordinal)
                .ToArray();
            foreach (string path in paths)
            {
                if (testBudget == 0)
                {
                    gaps.Add("Existing test context budget exhausted.");
                    break;
                }

                string content = await File.ReadAllTextAsync(path, cancellationToken);
                int allowed = Math.Min(content.Length, testBudget);
                Append(text, root, path, content, allowed);
                testBudget -= allowed;
            }
        }

        foreach (string path in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !WorkspacePath.HasLink(root, path))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).Take(20))
        {
            string content = await File.ReadAllTextAsync(path, cancellationToken);
            Append(text, root, path, content, Math.Min(content.Length, 4000));
        }

        // Product builds treat code-style warnings as errors, so generated tests must follow the same rules.
        string editorConfig = Path.Combine(root, ".editorconfig");
        if (File.Exists(editorConfig))
        {
            string content = await File.ReadAllTextAsync(editorConfig, cancellationToken);
            Append(text, root, editorConfig, content, Math.Min(content.Length, 8000));
        }

        HashSet<string> knownIds = ids.ToHashSet(StringComparer.Ordinal);
        string specPrefix = profile.SpecificationDirectory.Replace('\\', '/').TrimEnd('/') + "/";
        List<string> changedIds = [];
        bool inSpec = false;
        bool inHunk = false;
        foreach (string line in snapshot.Diff.Split('\n'))
        {
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                inSpec = false;
                inHunk = false;
            }
            else if (!inHunk && line.StartsWith("+++ ", StringComparison.Ordinal))
            {
                inSpec = line.StartsWith("+++ b/", StringComparison.Ordinal) &&
                    line[6..].TrimEnd('\r').StartsWith(specPrefix, StringComparison.Ordinal);
            }
            else if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                inHunk = true;
            }
            else if (inHunk && inSpec && line.StartsWith('+') &&
                RequirementIdRegex().Match(line[1..]) is { Success: true } match &&
                knownIds.Contains(match.Groups[1].Value))
            {
                changedIds.Add(match.Groups[1].Value);
            }
        }

        return new M2Context(text.ToString(), ids.Distinct(StringComparer.Ordinal).ToArray(),
            changedIds.Distinct(StringComparer.Ordinal).ToArray(), gaps);
    }

    private static void Append(StringBuilder text, string root, string path, string content, int maxLength)
    {
        text.Append("\n=== ").Append(Path.GetRelativePath(root, path)).AppendLine(" ===");
        text.AppendLine(Redact(content[..Math.Min(content.Length, maxLength)]));
    }

    private static string Resolve(string root, string relative)
    {
        if (Path.IsPathRooted(relative))
        {
            throw new InvalidOperationException("Absolute repository path is forbidden.");
        }

        string path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || WorkspacePath.HasLink(root, path))
        {
            throw new InvalidOperationException("Repository path escapes the workspace.");
        }

        return path;
    }

    private static bool Under(string path, string directory) =>
        path.StartsWith(directory.TrimEnd('/') + "/", StringComparison.Ordinal);

    private static string Redact(string content) => SecretAssignmentRegex().Replace(content, "$1=[REDACTED]");

    [GeneratedRegex(@"(?m)^\|\s*([A-Z][A-Z0-9]+(?:-[A-Z0-9]+)+)\s*\|")]
    private static partial Regex RequirementIdRegex();

    [GeneratedRegex("""(?im)\b(api[_-]?key|token|secret|password)\s*['"]?\s*[:=]\s*(?:['"][^'"]{8,}['"]|[A-Za-z0-9_./+=-]{8,}(?=\s*(?:[,;#]|$)))""")]
    private static partial Regex SecretAssignmentRegex();
}
