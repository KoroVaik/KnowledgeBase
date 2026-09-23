using System.Diagnostics;

namespace KnowledgeBase.Core.Observability;

public sealed record OperationContext(
    string? SessionId = null,
    string? UploadBatchId = null,
    string? UploadId = null,
    string? CausationId = null,
    string? JobId = null)
{
    private static readonly AsyncLocal<OperationContext?> Slot = new();
    public static OperationContext Current => Slot.Value ?? new();

    public static IDisposable Push(OperationContext context)
    {
        var previous = Slot.Value;
        Slot.Value = context;
        return new Restore(() => Slot.Value = previous);
    }

    public static string? Identifier(string? value) =>
        value is { Length: > 0 and <= 80 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
            ? value : null;

    public static Activity StartActivity(string name, string? parentId = null)
    {
        var activity = new Activity(name).SetIdFormat(ActivityIdFormat.W3C);
        if (ActivityContext.TryParse(parentId, null, out _)) activity.SetParentId(parentId!);
        return activity.Start();
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
