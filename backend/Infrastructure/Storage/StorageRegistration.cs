namespace Backend.Infrastructure.Storage;

public static class StorageRegistration
{
    public static IServiceCollection AddAssetStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));

        // Singleton so the assets directory is created once, at startup, rather than on the
        // first upload. Swapping in S3AssetStorage happens on this line and nowhere else.
        services.AddSingleton<IAssetStorage, LocalFileAssetStorage>();

        return services;
    }
}
