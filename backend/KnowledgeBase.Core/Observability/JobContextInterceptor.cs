using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using KnowledgeBase.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace KnowledgeBase.Core.Observability;

public sealed record JobTraceContext(OperationContext Operation, string? TraceParent)
{
    public static JobTraceContext Read(string? json)
    {
        try { return json is null ? new(new(), null) : JsonSerializer.Deserialize<JobTraceContext>(json) ?? new(new(), null); }
        catch (JsonException) { return new(new(), null); }
    }
}

public sealed class JobContextInterceptor(ILogger<JobContextInterceptor> logger) : SaveChangesInterceptor
{
    private readonly ConditionalWeakTable<DbContext, List<ProcessingJob>> pending = new();

    private void Prepare(DbContext? context)
    {
        if (context is null) return;
        var jobs = context.ChangeTracker.Entries<ProcessingJob>()
            .Where(entry => entry.State == EntityState.Added ||
                entry.Property(job => job.Status).IsModified && entry.Entity.Status == ProcessingStatus.Pending && entry.Entity.Attempts == 0 && OperationContext.Current.JobId != entry.Entity.Id)
            .Select(entry => entry.Entity).ToList();
        foreach (var job in jobs)
            job.DiagnosticContext = JsonSerializer.Serialize(new JobTraceContext(OperationContext.Current, Activity.Current?.Id));
        pending.Remove(context);
        pending.Add(context, jobs);
    }

    private void Completed(DbContext? context)
    {
        if (context is null || !pending.TryGetValue(context, out var jobs)) return;
        pending.Remove(context);
        foreach (var job in jobs)
            logger.LogInformation("Job queued: {JobId} {JobKind} {AssetId} parent {ParentJobId}", job.Id, job.Kind, job.AssetId, OperationContext.Current.JobId);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    { Prepare(eventData.Context); return result; }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    { Prepare(eventData.Context); return ValueTask.FromResult(result); }
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    { Completed(eventData.Context); return result; }
    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    { Completed(eventData.Context); return ValueTask.FromResult(result); }
}
