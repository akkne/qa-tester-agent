namespace QaAgent.Application.Abstractions;

public sealed record ProjectProfile(
    string Id,
    int Version,
    string SpecificationDirectory,
    IReadOnlyList<string> AllowedTestDirectories,
    string ReportDirectory);

public interface IProjectProfileProvider
{
    ProjectProfile GetRequired(string profileId);
}
