using QaAgent.Application.Abstractions;
using QaAgent.Infrastructure.Llm;

namespace QaAgent.Host.Composition;

public static class LlmServiceCollectionExtensions
{
    public static IServiceCollection AddLlmClient(this IServiceCollection services, IConfiguration configuration)
    {
        CodexExecOptions options = new()
        {
            Executable = configuration["Llm:CodexExecutable"] ?? "codex",
            Model = configuration["Llm:Model"],
            TimeoutSeconds = configuration.GetValue("Llm:TimeoutSeconds", 600)
        };

        if (string.IsNullOrWhiteSpace(options.Executable) || options.TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException("Llm:CodexExecutable and Llm:TimeoutSeconds must be valid.");
        }

        services.AddSingleton(options);
        services.AddSingleton<ICodexActivityStore>(new FileCodexActivityStore(
            configuration["Agent:ActivityRoot"] ?? "activity"));
        services.AddSingleton<ICodexExecRunner, CodexExecRunner>();
        services.AddTransient<ILlmClient, CodexExecLlmClient>();
        return services;
    }
}
