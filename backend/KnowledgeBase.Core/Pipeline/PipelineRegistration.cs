namespace KnowledgeBase.Core.Pipeline;

public static class PipelineRegistration
{
    public static IServiceCollection AddContentPipeline(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PipelineOptions>(configuration.GetSection(PipelineOptions.SectionName));
        services.AddHostedService<PipelineWorker>();

        return services;
    }
}
