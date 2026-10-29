using Microsoft.EntityFrameworkCore;
using QaAgent.Application;
using QaAgent.Application.Abstractions;
using QaAgent.Application.Analysis;
using QaAgent.Application.Execution;
using QaAgent.Host.Worker;
using QaAgent.Infrastructure.Analysis;
using QaAgent.Infrastructure.Execution;
using QaAgent.Infrastructure.Git;
using QaAgent.Infrastructure.Llm;
using QaAgent.Infrastructure.Persistence;
using QaAgent.Infrastructure.Publishing;
using QaAgent.Infrastructure.Time;

namespace QaAgent.Host.Composition;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddQaAgentCore(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddTransient<RunFactory>();
        return services;
    }

    public static IServiceCollection AddQaAgentPipeline(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");
        string? configuredSource = configuration["GitHub:SourcePath"];
        string source = string.IsNullOrWhiteSpace(configuredSource)
            ? $"https://github.com/{configuration["GitHub:Repository"]}.git"
            : configuredSource;
        string workspaceRoot = configuration["Agent:WorkspaceRoot"] ?? "worktrees";

        services.AddDbContext<RunDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IRunRepository, EfRunRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddSingleton(new GitWorkspaceOptions
        {
            Source = source,
            WorkspaceRoot = Path.GetFullPath(workspaceRoot)
        });
        services.AddSingleton<IGitWorkspace, GitCliWorkspace>();
        services.AddScoped<ICoverageCalculator, CoberturaCoverageCalculator>();
        services.AddScoped<IM3Executor>(provider => configuration["Profiles:demo:ExecutionMode"] switch
        {
            "lead-qualification" => new LeadQualificationM3Executor(configuration["Agent:ArtifactRoot"] ?? "artifacts"),
            "agent" => new AgentM3Executor(configuration["Agent:ArtifactRoot"] ?? "artifacts",
                TestAgentOptions(configuration),
                provider.GetRequiredService<ICoverageCalculator>(),
                provider.GetRequiredService<ICodexActivityStore>(),
                provider.GetRequiredService<ILogger<AgentM3Executor>>()),
            _ => new DockerM3Executor(configuration["Agent:ArtifactRoot"] ?? "artifacts",
                provider.GetRequiredService<ICoverageCalculator>())
        });
        services.AddScoped<IReportPublisher>(_ => new GitReportPublisher(
            configuration["Agent:ArtifactRoot"] ?? "artifacts",
            string.IsNullOrWhiteSpace(configuration["GitHub:PublishRemote"])
                ? $"https://github.com/{configuration["GitHub:Repository"]}.git"
                : configuration["GitHub:PublishRemote"]!,
            configuration["GitHub:Token"],
            string.IsNullOrWhiteSpace(configuration["Agent:ResultBaseUrl"])
                ? "http://127.0.0.1:5080"
                : configuration["Agent:ResultBaseUrl"]!));
        services.AddLlmClient(configuration);
        services.AddScoped<IM2ContextReader, M2ContextReader>();
        // Tests of every stack, C# included, are written by the Codex agent in the test-runner container.
        services.AddScoped<IM2TestWriter>(provider => new AgentTestWriter(
            configuration["Agent:ArtifactRoot"] ?? "artifacts",
            TestAgentOptions(configuration),
            provider.GetRequiredService<ICodexActivityStore>(),
            provider.GetRequiredService<ILogger<AgentTestWriter>>()));
        services.AddScoped<ISecretScanner, DiffSecretScanner>();
        services.AddScoped<M2Processor>();
        services.AddHostedService<RunWorker>();
        return services;
    }

    private static AgentExecutionOptions TestAgentOptions(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection("TestAgent");
        AgentExecutionOptions defaults = new();
        AgentExecutionOptions options = new()
        {
            Image = section["Image"] ?? defaults.Image,
            CodexHome = section["CodexHome"] ?? Environment.GetEnvironmentVariable("CODEX_HOME") ?? defaults.CodexHome,
            ApiKey = section["ApiKey"],
            Model = section["Model"] ?? configuration["Llm:Model"],
            TimeoutMinutes = section.GetValue("TimeoutMinutes", defaults.TimeoutMinutes),
            CommandTimeoutSeconds = section.GetValue("CommandTimeoutSeconds", defaults.CommandTimeoutSeconds),
            Cpus = section["Cpus"] ?? defaults.Cpus,
            Memory = section["Memory"] ?? defaults.Memory,
            AllowDocker = section.GetValue("AllowDocker", false),
            Hints = configuration.GetSection("Profiles:demo:TestHints").Get<string[]>() ?? []
        };
        if (string.IsNullOrWhiteSpace(options.Image) || options.TimeoutMinutes <= 0 || options.CommandTimeoutSeconds <= 0)
            throw new InvalidOperationException("TestAgent:Image, TimeoutMinutes and CommandTimeoutSeconds must be valid.");
        return options;
    }
}
