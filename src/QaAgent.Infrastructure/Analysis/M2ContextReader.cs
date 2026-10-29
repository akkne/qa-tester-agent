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
        string specDirectory = profile.SpecificationDirectory.Replace('\\', '/').TrimEnd('/') + "/";
        foreach (ChangedFile file in snapshot.Files)
        {
            if (!IsTextSource(file.Path) || file.Path.StartsWith(specDirectory, StringComparison.Ordinal) ||
                IsTestPath(profile, file.Path))
            {
                continue;
            }

            string path = Resolve(root, file.Path);
            if (File.Exists(path))
            {
                string content = await File.ReadAllTextAsync(path, cancellationToken);
                if (content.Contains('\0', StringComparison.Ordinal))
                {
                    continue;
                }

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
        HashSet<string> changedTests = snapshot.Files
            .Where(file => IsTextSource(file.Path) && IsTestPath(profile, file.Path))
            .Select(file => Resolve(root, file.Path))
            .ToHashSet(StringComparer.Ordinal);
        string[] testPaths = RepositoryFiles(root)
            .Where(path => IsTextSource(path) && IsTestPath(profile, Relative(root, path)))
            .OrderBy(path => !changedTests.Contains(path))
            .ThenBy(path => changedTests.Any(changed =>
                Path.GetDirectoryName(changed) == Path.GetDirectoryName(path)) ? 0 : 1)
            .ThenBy(path => path, StringComparer.Ordinal)
            .ToArray();
        foreach (string path in testPaths)
        {
            if (testBudget == 0)
            {
                gaps.Add("Existing test context budget exhausted.");
                break;
            }

            string content = await File.ReadAllTextAsync(path, cancellationToken);
            if (content.Contains('\0', StringComparison.Ordinal))
            {
                continue;
            }

            int allowed = Math.Min(content.Length, testBudget);
            Append(text, root, path, content, allowed);
            testBudget -= allowed;
        }

        // Manifests tell the model which language, test framework and assertion library the repository uses.
        foreach (string path in RepositoryFiles(root)
            .Where(path => Manifests.Contains(Path.GetFileName(path)) ||
                path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                ConfigRegex().IsMatch(Path.GetFileName(path)))
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

    // An explicit test directory holds test code and its helpers; with "." only files named like tests count.
    private static bool IsTestPath(ProjectProfile profile, string path)
    {
        string normalized = path.Replace('\\', '/');
        return profile.AllowedTestDirectories.Any(directory =>
        {
            string prefix = directory.Replace('\\', '/').Trim('/');
            return prefix is "" or "."
                ? TestNameRegex().IsMatch(normalized)
                : normalized.StartsWith(prefix + "/", StringComparison.Ordinal);
        });
    }

    private static bool IsTextSource(string path) =>
        !BinaryExtensions.Contains(Path.GetExtension(path)) && !LockFiles.Contains(Path.GetFileName(path));

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static IEnumerable<string> RepositoryFiles(string root)
    {
        Stack<string> pending = new([root]);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            foreach (string child in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal))
            {
                if (!IgnoredDirectories.Contains(Path.GetFileName(child)) && !WorkspacePath.HasLink(root, child))
                {
                    pending.Push(child);
                }
            }

            foreach (string file in Directory.EnumerateFiles(directory).Order(StringComparer.Ordinal))
            {
                if (!WorkspacePath.HasLink(root, file))
                {
                    yield return file;
                }
            }
        }
    }

    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.Ordinal)
    {
        ".git", "node_modules", "bin", "obj", "target", "dist", "build", "vendor", ".venv", "venv", "__pycache__",
        ".gradle", ".idea", ".vs", "coverage", ".next", ".tox", ".mypy_cache", ".pytest_cache"
    };

    private static readonly HashSet<string> Manifests = new(StringComparer.Ordinal)
    {
        "package.json", "pyproject.toml", "setup.cfg", "setup.py", "pytest.ini", "tox.ini", "requirements-dev.txt",
        "go.mod", "Cargo.toml", "pom.xml", "build.gradle", "build.gradle.kts", "composer.json", "phpunit.xml",
        "phpunit.xml.dist", "Gemfile", ".rspec", "mix.exs", "Package.swift", "CMakeLists.txt", "deno.json", "tsconfig.json"
    };

    private static readonly HashSet<string> LockFiles = new(StringComparer.Ordinal)
    {
        "package-lock.json", "yarn.lock", "pnpm-lock.yaml", "poetry.lock", "uv.lock", "Pipfile.lock", "Cargo.lock",
        "go.sum", "composer.lock", "Gemfile.lock", "packages.lock.json", "mix.lock", "Package.resolved"
    };

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".ico", ".webp", ".bmp", ".pdf", ".zip", ".gz", ".tgz", ".tar", ".7z", ".jar",
        ".war", ".class", ".dll", ".exe", ".so", ".dylib", ".a", ".o", ".pyc", ".woff", ".woff2", ".ttf", ".otf", ".eot",
        ".mp3", ".mp4", ".mov", ".wav", ".bin", ".snk", ".pfx", ".p12", ".db", ".sqlite"
    };

    private static string Redact(string content) => SecretAssignmentRegex().Replace(content, "$1=[REDACTED]");

    [GeneratedRegex(@"(^|/)(tests?|__tests__|specs?|testing)/|(^|/)test_[^/]*$|[._-](test|tests|spec)\.[^/]+$|Tests?\.[^/.]+$|_test\.go$")]
    private static partial Regex TestNameRegex();

    [GeneratedRegex(@"^(jest|vitest|playwright|karma|mocha|\.mocharc)(\.config)?\.(js|cjs|mjs|ts|json|ya?ml)$")]
    private static partial Regex ConfigRegex();

    [GeneratedRegex(@"(?m)^\|\s*([A-Z][A-Z0-9]+(?:-[A-Z0-9]+)+)\s*\|")]
    private static partial Regex RequirementIdRegex();

    [GeneratedRegex("""(?im)\b(api[_-]?key|token|secret|password)\s*['"]?\s*[:=]\s*(?:['"][^'"]{8,}['"]|[A-Za-z0-9_./+=-]{8,}(?=\s*(?:[,;#]|$)))""")]
    private static partial Regex SecretAssignmentRegex();
}
