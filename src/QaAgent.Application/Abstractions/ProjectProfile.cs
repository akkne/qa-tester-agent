namespace QaAgent.Application.Abstractions;

public sealed record ProjectProfile(
    string Id,
    int Version,
    string SpecificationDirectory,
    IReadOnlyList<string> AllowedTestDirectories,
    string ReportDirectory)
{
    /// <summary>
    /// True for a relative path without "." or ".." segments inside an allowed test directory. The directory "."
    /// allows the whole repository, for stacks that keep tests next to the code (Go, Rust unit tests).
    /// </summary>
    public bool AllowsTestPath(string path)
    {
        string normalized = path.Replace('\\', '/');
        if (normalized.Length == 0 || normalized.StartsWith('/') || Path.IsPathRooted(normalized) ||
            normalized.Split('/').Any(part => part is "" or "." or ".."))
            return false;
        return AllowedTestDirectories.Any(directory =>
        {
            string prefix = directory.Replace('\\', '/').Trim('/');
            return prefix is "" or "." || normalized.StartsWith(prefix + "/", StringComparison.Ordinal);
        });
    }
}
