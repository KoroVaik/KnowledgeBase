using KnowledgeBase.Core.Ai.Configuration;

namespace KnowledgeBase.Core.Ai;

public static class AiRegistration
{
    public static IServiceCollection AddContentAnalyzer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(OllamaOptions.SectionName);

        services
            .AddOptions<OllamaOptions>()
            .Bind(section)
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.BaseUrl)
                    && !string.IsNullOrWhiteSpace(options.Model),
                "Ai:Ollama needs BaseUrl and Model.")
            .ValidateOnStart();

        // Bound a second time by hand: BaseUrl and Timeout are needed to configure the typed
        // client, and IOptions cannot be resolved while the container is still being built.
        var options = section.Get<OllamaOptions>() ?? new OllamaOptions();

        services.AddHttpClient<IContentAnalyzer, OllamaAnalyzer>(client =>
        {
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = options.Timeout;
        });

        return services;
    }
}
