using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Core.Storage;

public static class StorageRegistration
{
    public static IServiceCollection AddAssetStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));

        // A bucket is the only storage there is: the browser PUTs and GETs the bytes itself,
        // and an instance that cannot sign those URLs cannot serve files at all.
        services
            .AddOptions<S3StorageOptions>()
            .Bind(configuration.GetSection(S3StorageOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ServiceUrl)
                    && !string.IsNullOrWhiteSpace(options.BucketName)
                    && !string.IsNullOrWhiteSpace(options.AccessKeyId)
                    && !string.IsNullOrWhiteSpace(options.SecretAccessKey),
                "Storage:S3 needs ServiceUrl, BucketName, AccessKeyId and SecretAccessKey.")
            // Without this the app starts happily and only the first request reveals the gap.
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
        services.AddSingleton<IAssetLinkSigner, S3AssetLinkSigner>();
        services.AddSingleton<IAssetContentReader, S3AssetContentReader>();

        return services;
    }
}
