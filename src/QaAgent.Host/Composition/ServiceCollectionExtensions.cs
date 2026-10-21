using QaAgent.Application;
using QaAgent.Application.Abstractions;
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
}
