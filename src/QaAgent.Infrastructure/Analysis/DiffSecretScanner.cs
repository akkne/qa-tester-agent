using System.Text.RegularExpressions;
using QaAgent.Application.Abstractions;

namespace QaAgent.Infrastructure.Analysis;

public sealed partial class DiffSecretScanner : ISecretScanner
{
    public Task<IReadOnlyList<SecretFinding>> ScanAsync(GitSnapshot snapshot, CancellationToken cancellationToken)
    {
        List<SecretFinding> findings = [];
        string? path = null;
        int lineNumber = 0;
        foreach (string line in snapshot.Diff.Split('\n'))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (line.StartsWith("+++ b/", StringComparison.Ordinal))
            {
                path = line[6..].TrimEnd('\r');
            }
            else if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                Match match = HunkRegex().Match(line);
                if (match.Success)
                {
                    lineNumber = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            else if (line.StartsWith('+') && !line.StartsWith("+++", StringComparison.Ordinal))
            {
                string added = line[1..];
                string? kind = TokenRegex().IsMatch(added) ? "credential token" :
                    PrivateKeyRegex().IsMatch(added) ? "private key" :
                    (!added.Contains("demo-local-only", StringComparison.Ordinal) &&
                        !(path is not null && path.EndsWith(".example", StringComparison.Ordinal) &&
                            added.Contains("Password=offline-test-only", StringComparison.Ordinal)) &&
                        AssignmentRegex().IsMatch(added))
                        ? "secret assignment" : null;
                if (kind is not null && path is not null)
                {
                    findings.Add(new SecretFinding(path, lineNumber, kind));
                }

                lineNumber++;
            }
            else if (!line.StartsWith('-'))
            {
                lineNumber++;
            }
        }

        return Task.FromResult<IReadOnlyList<SecretFinding>>(findings);
    }

    [GeneratedRegex(@"\+(\d+)(?:,\d+)?\s+@@")]
    private static partial Regex HunkRegex();

    [GeneratedRegex(@"\b(?:gh[pousr]_[A-Za-z0-9_]{20,}|sk-[A-Za-z0-9_-]{20,}|AKIA[0-9A-Z]{16})\b")]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----")]
    private static partial Regex PrivateKeyRegex();

    // Require a literal value. A variable or method call named "password"/"token" is not a leaked secret.
    [GeneratedRegex("""(?i)\b(?:api[_-]?key|token|secret|password)\s*['"]?\s*[:=]\s*(?:['"][^'"]{8,}['"]|[A-Za-z0-9_./+=-]{8,}(?=\s*(?:[,;#]|$)))""")]
    private static partial Regex AssignmentRegex();
}
