using Microsoft.EntityFrameworkCore;
using QaAgent.Application;
using QaAgent.Application.Abstractions;
using QaAgent.Application.Analysis;
using QaAgent.Host.Worker;
using QaAgent.Infrastructure.Analysis;
using QaAgent.Infrastructure.Git;
using QaAgent.Infrastructure.Persistence;
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
        if (!string.IsNullOrWhiteSpace(configuration["Llm:Model"]))
        {
            services.AddLlmClient(configuration);
            services.AddScoped<IM2ContextReader, M2ContextReader>();
            services.AddScoped<IM2TestBuilder, M2TestBuilder>();
            services.AddScoped<ISecretScanner, DiffSecretScanner>();
            services.AddScoped<M2Processor>();
        }
        services.AddHostedService<RunWorker>();
        return services;
    }
}
