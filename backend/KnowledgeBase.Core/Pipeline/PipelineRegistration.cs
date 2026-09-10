using KnowledgeBase.Core.Pipeline.Extraction;

namespace KnowledgeBase.Core.Pipeline;

public static class PipelineRegistration
{
    public static IServiceCollection AddContentPipeline(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PipelineOptions>(configuration.GetSection(PipelineOptions.SectionName));

        // The always-available extractors. Format-specific ones with their own dependencies
        // (PdfSourceExtractor and PdfPig) are added by the composition root that needs them, so
        // they stay out of the API's deployed image.
        services.AddSingleton<ISourceExtractor, TextSourceExtractor>();
        services.AddSingleton<ISourceExtractor, ImageSourceExtractor>();
        services.AddSingleton<SourceExtractorSelector>();

        services.AddHostedService<PipelineWorker>();

        return services;
    }
}
