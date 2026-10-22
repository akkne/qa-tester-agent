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
        foreach (string spec in Directory.EnumerateFiles(specRoot, "*.md", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
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

            foreach (string path in Directory.EnumerateFiles(testRoot, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
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
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).Take(20))
        {
            string content = await File.ReadAllTextAsync(path, cancellationToken);
            Append(text, root, path, content, Math.Min(content.Length, 4000));
        }

        string[] changedIds = snapshot.Diff.Split('\n')
            .Where(line => line.StartsWith('+') && !line.StartsWith("+++", StringComparison.Ordinal))
            .Select(line => RequirementIdRegex().Match(line[1..]))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal).ToArray();
        return new M2Context(text.ToString(), ids.Distinct(StringComparer.Ordinal).ToArray(), changedIds, gaps);
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
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
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

    [GeneratedRegex(@"(?im)\b(api[_-]?key|token|secret|password)\s*[:=]\s*[^\s,;]+")]
    private static partial Regex SecretAssignmentRegex();
}
