using Amazon.Runtime;
using Amazon.S3;
using Backend.Infrastructure.Features;
using Microsoft.Extensions.Options;

namespace Backend.Infrastructure.Storage;

public static class StorageRegistration
{
    public static IServiceCollection AddAssetStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(StorageOptions.SectionName);
        services.Configure<StorageOptions>(section);

        // Singleton in both cases: the local one creates its directory at startup rather than
        // on the first upload, and an S3 client is meant to be shared and reused.
        if (section.GetValue<AssetStorageProvider>(nameof(StorageOptions.Provider)) == AssetStorageProvider.S3)
        {
            services.AddS3AssetStorage(configuration);
        }
        else
        {
            // Signing needs a bucket. Caught here rather than at the first click, where it
            // would look like a broken endpoint instead of a contradictory configuration.
            if (configuration.GetValue<bool>(
                    $"{FeatureOptions.SectionName}:{nameof(FeatureOptions.DirectAssetAccessEnabled)}"))
            {
                throw new InvalidOperationException(
                    "Features:DirectAssetAccessEnabled needs Storage:Provider=S3 - only a bucket can sign links.");
            }

            services.AddSingleton<IAssetStorage, LocalFileAssetStorage>();
        }

        return services;
    }

    private static void AddS3AssetStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<S3StorageOptions>()
            .Bind(configuration.GetSection(S3StorageOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ServiceUrl)
                    && !string.IsNullOrWhiteSpace(options.BucketName)
                    && !string.IsNullOrWhiteSpace(options.AccessKeyId)
                    && !string.IsNullOrWhiteSpace(options.SecretAccessKey),
                "Storage:S3 needs ServiceUrl, BucketName, AccessKeyId and SecretAccessKey.")
            // Without this the app starts happily and only the first upload reveals the gap.
            .ValidateOnStart();

        services.AddSingleton<IAmazonS3>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<S3StorageOptions>>().Value;

            var config = new AmazonS3Config
            {
                ServiceURL = options.ServiceUrl,
                ForcePathStyle = options.ForcePathStyle,
                AuthenticationRegion = options.Region,

                // SDK v4 defaults to WHEN_SUPPORTED, which turns every upload into a chunked
                // body with a trailing checksum. Garage v2.3.0 answers that with
                // "Invalid payload signature".
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            };

            return new AmazonS3Client(
                new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey),
                config);
        });

        services.AddSingleton<IAssetStorage, S3AssetStorage>();

        // Registered only on this branch: its absence from the container is what tells the
        // rest of the app that nothing here can sign.
        services.AddSingleton<IAssetLinkSigner, S3AssetLinkSigner>();
    }
}
