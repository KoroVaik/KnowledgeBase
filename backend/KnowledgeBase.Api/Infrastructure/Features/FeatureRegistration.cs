namespace KnowledgeBase.Api.Infrastructure.Features;

public static class FeatureRegistration
{
    public static IServiceCollection AddFeatureFlags(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bound to the live section rather than a snapshot of it, so IOptionsSnapshot sees a
        // flag flipped in appsettings.json without a restart.
        services.Configure<FeatureOptions>(configuration.GetSection(FeatureOptions.SectionName));

        return services;
    }
}
