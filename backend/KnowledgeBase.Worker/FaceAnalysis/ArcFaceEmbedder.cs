using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using KnowledgeBase.Core.FaceAnalysis;

namespace KnowledgeBase.Worker.FaceAnalysis;

// insightface w600k_r50 (ArcFace ResNet50): a 512-d embedding of the 112x112 crop that a
// similarity transform warps so the five landmarks land on the standard template. Input is
// RGB normalized to [-1, 1]; the same person scores far from other people on this embedding.
public sealed class ArcFaceEmbedder(string modelPath) : IDisposable
{
    // Standard landmark positions (left eye, right eye, nose, mouth corners) for the 112x112 input.
    private static readonly (float X, float Y)[] Template =
    [
        (38.2946f, 51.6963f), (73.5318f, 51.5014f), (56.0252f, 71.7366f), (41.5493f, 92.3655f), (70.7299f, 92.2041f),
    ];

    private const int Side = 112;

    private readonly InferenceSession _session = new(modelPath);

    public void Dispose() => _session.Dispose();

    public float[] Embed(Image<Rgb24> image, IReadOnlyList<FaceLandmark> landmarks)
    {
        if (landmarks.Count != Template.Length)
            throw new InvalidOperationException($"Face alignment needs {Template.Length} landmarks, got {landmarks.Count}.");

        var (a, b, tx, ty) = FitSimilarity(
            [.. landmarks.Select(landmark => (landmark.X, landmark.Y))], Template);
        // Inverse of the transform, so each output pixel can be sampled from the source image.
        var d = a * a + b * b;
        var ia = a / d; var ib = -b / d;
        var itx = (b * ty - a * tx) / d; var ity = (b * tx - a * ty) / d;

        var tensor = new DenseTensor<float>([1, 3, Side, Side]);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < Side; y++)
            {
                for (var x = 0; x < Side; x++)
                {
                    var sx = ia * (x + 0.5f) - ib * (y + 0.5f) + itx;
                    var sy = ib * (x + 0.5f) + ia * (y + 0.5f) + ity;
                    var (r, g, bl) = SampleBilinear(accessor, sx, sy);
                    tensor[0, 0, y, x] = (r - 127.5f) / 127.5f;
                    tensor[0, 1, y, x] = (g - 127.5f) / 127.5f;
                    tensor[0, 2, y, x] = (bl - 127.5f) / 127.5f;
                }
            }
        });

        var input = NamedOnnxValue.CreateFromTensor(_session.InputMetadata.Keys.Single(), tensor);
        using var results = _session.Run([input]);
        return results.Single().AsEnumerable<float>().ToArray();
    }

    private static (float R, float G, float B) SampleBilinear(PixelAccessor<Rgb24> accessor, float x, float y)
    {
        var clampedX = Math.Clamp(x, 0f, accessor.Width - 1);
        var clampedY = Math.Clamp(y, 0f, accessor.Height - 1);
        var x0 = (int)clampedX; var y0 = (int)clampedY;
        var x1 = Math.Min(x0 + 1, accessor.Width - 1); var y1 = Math.Min(y0 + 1, accessor.Height - 1);
        var fx = clampedX - x0; var fy = clampedY - y0;
        var top = Blend(accessor.GetRowSpan(y0)[x0], accessor.GetRowSpan(y0)[x1], fx);
        var bottom = Blend(accessor.GetRowSpan(y1)[x0], accessor.GetRowSpan(y1)[x1], fx);
        return (Lerp(top.R, bottom.R, fy), Lerp(top.G, bottom.G, fy), Lerp(top.B, bottom.B, fy));
    }

    private static (float R, float G, float B) Blend(Rgb24 left, Rgb24 right, float t) =>
        (Lerp(left.R, right.R, t), Lerp(left.G, right.G, t), Lerp(left.B, right.B, t));

    private static float Lerp(float first, float second, float t) => first + (second - first) * t;

    // Least squares for dst = (a*x - b*y + tx, b*x + a*y + ty): rotation+scale+translation
    // with no shear or reflection, the transform class skimage's SimilarityTransform also fits.
    private static (float A, float B, float Tx, float Ty) FitSimilarity(
        (float X, float Y)[] source, (float X, float Y)[] target)
    {
        var normal = new double[4, 4];
        var rhs = new double[4];
        for (var index = 0; index < source.Length; index++)
        {
            var (x, y) = source[index];
            var (u, v) = target[index];
            var row0 = new[] { x, -y, 1.0, 0.0 };
            var row1 = new[] { y, x, 0.0, 1.0 };
            AddRow(normal, rhs, row0, u);
            AddRow(normal, rhs, row1, v);
        }

        var solution = Solve(normal, rhs);
        return ((float)solution[0], (float)solution[1], (float)solution[2], (float)solution[3]);
    }

    private static void AddRow(double[,] normal, double[] rhs, double[] row, double expected)
    {
        for (var i = 0; i < 4; i++)
        {
            rhs[i] += row[i] * expected;
            for (var j = 0; j < 4; j++) normal[i, j] += row[i] * row[j];
        }
    }

    private static double[] Solve(double[,] matrix, double[] vector)
    {
        var size = vector.Length;
        var augmented = new double[size, size + 1];
        for (var i = 0; i < size; i++)
        {
            for (var j = 0; j < size; j++) augmented[i, j] = matrix[i, j];
            augmented[i, size] = vector[i];
        }

        for (var column = 0; column < size; column++)
        {
            var pivot = Enumerable.Range(column, size - column).MaxBy(row => Math.Abs(augmented[row, column]));
            if (Math.Abs(augmented[pivot, column]) < 1e-12) throw new InvalidOperationException("Degenerate face alignment: the landmarks do not define a transform.");
            for (var j = 0; j <= size; j++) (augmented[column, j], augmented[pivot, j]) = (augmented[pivot, j], augmented[column, j]);
            for (var row = column + 1; row < size; row++)
            {
                var factor = augmented[row, column] / augmented[column, column];
                for (var j = column; j <= size; j++) augmented[row, j] -= factor * augmented[column, j];
            }
        }

        var result = new double[size];
        for (var row = size - 1; row >= 0; row--)
        {
            var sum = augmented[row, size] - Enumerable.Range(row + 1, size - row - 1).Sum(j => augmented[row, j] * result[j]);
            result[row] = sum / augmented[row, row];
        }
        return result;
    }
}
