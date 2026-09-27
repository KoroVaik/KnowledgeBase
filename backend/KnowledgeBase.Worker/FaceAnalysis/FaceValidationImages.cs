using System.Security.Cryptography;
using System.Text;
using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.FaceAnalysis;
using KnowledgeBase.Core.Persistence;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace KnowledgeBase.Worker.FaceAnalysis;

public static class FaceValidationImages
{
    public static string InputHash(FaceOccurrence face, string contentHash) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(FormattableString.Invariant($"{FaceValidationPolicy.PipelineVersion}|{contentHash}|{face.X}|{face.Y}|{face.Width}|{face.Height}"))));

    public static void Measure(Image<Rgb24> image, FaceOccurrence face, FaceValidation validation)
    {
        using var crop = image.Clone(ctx => ctx.Crop(Bounds(image, face, 1)));
        validation.MinSidePixels = Math.Min(crop.Width, crop.Height);
        validation.TouchesImageEdge = face.X <= 1 || face.Y <= 1 || (long)face.X + face.Width >= image.Width - 1 || (long)face.Y + face.Height >= image.Height - 1;
        crop.Mutate(ctx => ctx.Resize(112, 112, KnownResamplers.Bicubic));
        double Gray(int x, int y) { var p = crop[x, y]; return .299 * p.R + .587 * p.G + .114 * p.B; }
        double sum = 0, squares = 0;
        var count = 0;
        for (var y = 11; y < 100; y++)
        for (var x = 11; x < 100; x++)
        {
            var lap = Gray(x - 1, y) + Gray(x + 1, y) + Gray(x, y - 1) + Gray(x, y + 1) - 4 * Gray(x, y);
            sum += lap; squares += lap * lap; count++;
        }
        validation.Sharpness112 = Math.Max(0, squares / count - Math.Pow(sum / count, 2));
    }

    public static (AnalysisImage Crop, AnalysisImage Context) Prepare(Image<Rgb24> image, FaceOccurrence face)
    {
        var faceBounds = Bounds(image, face, 1);
        var contextBounds = Bounds(image, face, 2.5);
        using var crop = image.Clone(ctx => ctx.Crop(faceBounds));
        using var context = image.Clone(ctx => ctx.Crop(contextBounds));
        Resize(crop, 256);
        Resize(context, 448);
        var sx = (double)context.Width / contextBounds.Width;
        var sy = (double)context.Height / contextBounds.Height;
        var left = Math.Clamp((int)Math.Round((faceBounds.Left - contextBounds.Left) * sx), 0, context.Width - 1);
        var top = Math.Clamp((int)Math.Round((faceBounds.Top - contextBounds.Top) * sy), 0, context.Height - 1);
        var right = Math.Clamp((int)Math.Round((faceBounds.Right - contextBounds.Left) * sx), left, context.Width - 1);
        var bottom = Math.Clamp((int)Math.Round((faceBounds.Bottom - contextBounds.Top) * sy), top, context.Height - 1);
        for (var y = top; y <= bottom; y++)
        for (var x = left; x <= right; x++)
            if (x - left < 2 || right - x < 2 || y - top < 2 || bottom - y < 2) context[x, y] = new Rgb24(255, 220, 0);
        return (ToImage(crop), ToImage(context));
    }

    private static Rectangle Bounds(Image<Rgb24> image, FaceOccurrence face, double scale)
    {
        if (face.Width <= 0 || face.Height <= 0 || face.X >= image.Width || face.Y >= image.Height || (long)face.X + face.Width <= 0 || (long)face.Y + face.Height <= 0)
            throw new InvalidOperationException("The detected region does not intersect the original image.");
        var left = Math.Clamp((int)Math.Floor(face.X + face.Width * (1 - scale) / 2), 0, image.Width - 1);
        var top = Math.Clamp((int)Math.Floor(face.Y + face.Height * (1 - scale) / 2), 0, image.Height - 1);
        var right = Math.Clamp((int)Math.Ceiling(face.X + face.Width * (1 + scale) / 2), left + 1, image.Width);
        var bottom = Math.Clamp((int)Math.Ceiling(face.Y + face.Height * (1 + scale) / 2), top + 1, image.Height);
        return new Rectangle(left, top, right - left, bottom - top);
    }

    private static void Resize(Image<Rgb24> image, int longSide)
    {
        var scale = (double)longSide / Math.Max(image.Width, image.Height);
        image.Mutate(ctx => ctx.Resize(Math.Max(1, (int)Math.Round(image.Width * scale)), Math.Max(1, (int)Math.Round(image.Height * scale)), KnownResamplers.Bicubic));
    }

    private static AnalysisImage ToImage(Image<Rgb24> image)
    {
        using var buffer = new MemoryStream();
        image.SaveAsPng(buffer);
        return new(buffer.ToArray(), "image/png");
    }
}
