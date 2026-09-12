using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace KnowledgeBase.Core.Pipeline.Extraction;

public sealed record PhotoCaptureMetadata(DateTime? CapturedAtUtc, double? Latitude, double? Longitude)
{
    public static readonly PhotoCaptureMetadata Empty = new(null, null, null);
}

// EXIF only, best-effort: a screenshot, a PNG, or a photo a messenger recompressed carries none
// of this - a normal result, not a failure, so every exception here is swallowed rather than
// failing the pipeline job over data the model does not need anyway.
public static class PhotoCaptureReader
{
    public static PhotoCaptureMetadata Read(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var directories = ImageMetadataReader.ReadMetadata(stream);

            // DateTimeOriginal has no timezone of its own (the camera writes local time); we
            // store it as UTC anyway since a same-day/month grouping does not need precision.
            DateTime? capturedAt = directories
                .OfType<ExifSubIfdDirectory>()
                .Select(directory => directory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var value)
                    ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                    : (DateTime?)null)
                .FirstOrDefault(value => value is not null);

            GeoLocation? location = directories
                .OfType<GpsDirectory>()
                .Select(directory => directory.GetGeoLocation())
                .FirstOrDefault(geo => geo is { IsZero: false });

            return new PhotoCaptureMetadata(capturedAt, location?.Latitude, location?.Longitude);
        }
        catch
        {
            return PhotoCaptureMetadata.Empty;
        }
    }
}
