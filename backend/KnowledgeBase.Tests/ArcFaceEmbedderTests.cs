using KnowledgeBase.Core.FaceAnalysis;
using KnowledgeBase.Worker.FaceAnalysis;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace KnowledgeBase.Tests;

public sealed class ArcFaceEmbedderTests
{
    private static readonly FaceLandmark[] Template =
    [
        new(38.2946f, 51.6963f), new(73.5318f, 51.5014f), new(56.0252f, 71.7366f),
        new(41.5493f, 92.3655f), new(70.7299f, 92.2041f),
    ];

    [Theory]
    [InlineData(0f, 1f, 0f, 0f)]
    [InlineData(0f, 1f, 30f, 40f)]
    [InlineData(20f, 1.25f, 80f, 15f)]
    [InlineData(-25f, 0.9f, 15f, 90f)]
    [InlineData(70f, 0.7f, 120f, 10f)]
    public void AlignmentSamplesTheFaceAtItsActualPosition(float degrees, float scale, float tx, float ty)
    {
        var radians = degrees * MathF.PI / 180;
        var cosine = scale * MathF.Cos(radians);
        var sine = scale * MathF.Sin(radians);
        FaceLandmark SourcePoint(float x, float y) => new(cosine * x - sine * y + tx, sine * x + cosine * y + ty);

        using var image = new Image<Rgb24>(256, 256);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++) row[x] = new Rgb24((byte)x, (byte)y, 64);
            }
        });
        var landmarks = Template.Select(point => SourcePoint(point.X, point.Y)).ToArray();

        var tensor = ArcFaceEmbedder.CreateInputTensor(image, landmarks);

        Assert.Equal(new[] { 1, 3, 112, 112 }, tensor.Dimensions.ToArray());
        for (var y = 16; y < 112; y += 32)
        for (var x = 16; x < 112; x += 32)
        {
            var source = SourcePoint(x + 0.5f, y + 0.5f);
            Assert.InRange(MathF.Abs(tensor[0, 0, y, x] - (source.X - 127.5f) / 127.5f), 0, 0.0001f);
            Assert.InRange(MathF.Abs(tensor[0, 1, y, x] - (source.Y - 127.5f) / 127.5f), 0, 0.0001f);
            Assert.InRange(MathF.Abs(tensor[0, 2, y, x] - (64 - 127.5f) / 127.5f), 0, 0.0001f);
        }
    }
}
