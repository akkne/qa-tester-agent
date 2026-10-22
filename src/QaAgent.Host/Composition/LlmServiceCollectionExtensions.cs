using QaAgent.Application.Abstractions;
using QaAgent.Infrastructure.Llm;

namespace QaAgent.Host.Composition;

public static class LlmServiceCollectionExtensions
{
    public static IServiceCollection AddLlmClient(this IServiceCollection services, IConfiguration configuration)
    {
        LlmClientOptions options = new()
        {
            BaseUrl = configuration["Llm:BaseUrl"] ?? "https://api.openai.com/v1/",
            Model = configuration["Llm:Model"] ?? "",
            ApiKey = configuration["Llm:ApiKey"],
            TimeoutSeconds = configuration.GetValue("Llm:TimeoutSeconds", 120),
            MaxOutputTokens = configuration.GetValue("Llm:MaxOutputTokens", 4096)
        };

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out Uri? baseUri) ||
            baseUri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment))
        {
            throw new InvalidOperationException("Llm:BaseUrl must be an absolute HTTP(S) URL without a query or fragment.");
        }

        if (string.IsNullOrWhiteSpace(options.Model) || options.TimeoutSeconds <= 0 || options.MaxOutputTokens <= 0)
        {
            throw new InvalidOperationException("Llm:Model, Llm:TimeoutSeconds and Llm:MaxOutputTokens must be valid.");
        }

        services.AddSingleton(options);
        services.AddHttpClient<ILlmClient, OpenAiCompatibleLlmClient>(client =>
        {
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });
        return services;
    }
}
