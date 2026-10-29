namespace QaAgent.Infrastructure.Analysis;

internal static class WorkspacePath
{
    // A planted symlink (file or directory segment) must not redirect reads or writes outside the given root.
    public static bool HasLink(string root, string path)
    {
        for (string? current = path; current is not null && current.Length > root.Length; current = Path.GetDirectoryName(current))
        {
            FileInfo info = new(current);
            if (info.LinkTarget is not null || (info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint)))
            {
                return true;
            }
        }

        return false;
    }
}
