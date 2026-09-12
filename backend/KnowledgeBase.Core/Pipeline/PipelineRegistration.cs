using KnowledgeBase.Core.Pipeline.Extraction;
using KnowledgeBase.Core.Pipeline.SourceNotes;
using KnowledgeBase.Core.Pipeline.Synthesis;
using KnowledgeBase.Core.Pipeline.TagGrouping;
using KnowledgeBase.Core.Pipeline.TagHierarchy;

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

        // Handlers hold a scoped DbContext, so they and the selector over them are scoped too.
        services.AddScoped<IPipelineHandler, SourceNoteHandler>();
        services.AddScoped<IPipelineHandler, SynthesisHandler>();
        services.AddScoped<IPipelineHandler, TagGroupingHandler>();
        services.AddScoped<IPipelineHandler, TagHierarchyHandler>();
        services.AddScoped<PipelineHandlerSelector>();

        services.AddHostedService<PipelineWorker>();

        return services;
    }
}
