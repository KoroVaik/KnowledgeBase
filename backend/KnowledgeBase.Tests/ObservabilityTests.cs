using System.Diagnostics;
using System.Text.Json;
using KnowledgeBase.Api.Controllers.Diagnostics;
using KnowledgeBase.Core.Observability;
using KnowledgeBase.Core.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog.Events;
using Xunit;

namespace KnowledgeBase.Tests;

public sealed class ObservabilityTests
{
    [Fact]
    public async Task RotatingFilesReceiveDebugEventsAndRedactSecretsBeforeWriting()
    {
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        var directory = Path.Combine(temporaryRoot, "kb-logging-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            { ["Observability:MinimumLevel"] = "Debug", ["Observability:LogDirectory"] = directory });
            builder.AddObservability("tests");
            Serilog.Log.ForContext("ApiKey", "private-value").Debug("Debug event");
            Serilog.Log.Information("Request to {Url}", "https://example.test/object?signature=private-value");
            await Serilog.Log.CloseAndFlushAsync();
            var debugFile = Assert.Single(Directory.GetFiles(directory, "tests-debug-*.jsonl"));
            Assert.Contains("Debug event", await File.ReadAllTextAsync(debugFile));
            var all = string.Join("\n", Directory.GetFiles(directory).Select(File.ReadAllText));
            Assert.Contains("Request to", all);
            Assert.DoesNotContain("private-value", all);
        }
        finally
        {
            await Serilog.Log.CloseAndFlushAsync();
            if (Directory.Exists(directory) && Path.GetFullPath(directory).StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SecretsAreRemovedFromUrlsExceptionsAndStructuredProperties()
    {
        Assert.DoesNotContain("private", LogRedaction.Text("GET https://bucket.example/photo?X-Amz-Signature=private failed Password=private; Bearer private"));
        Assert.DoesNotContain("private", LogRedaction.Text("{\"password\":\"private with spaces\"}"));
        Assert.Equal("[redacted]", ((ScalarValue)LogRedaction.NamedProperty("ApiKey", new ScalarValue("private")).Value).Value);
        var structure = new StructureValue([new LogEventProperty("Password", new ScalarValue("private"))]);
        Assert.DoesNotContain("private", LogRedaction.Property(structure).ToString());
    }

    [Fact]
    public async Task QueueContextSurvivesReloadAndAutomaticRetriesButChangesOnManualRetry()
    {
        var interceptor = new JobContextInterceptor(NullLogger<JobContextInterceptor>.Instance);
        var options = new DbContextOptionsBuilder<KnowledgeBaseDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(interceptor).Options;
        string original;
        using (OperationContext.Push(new(SessionId: "session", UploadBatchId: "batch", UploadId: "upload")))
        using (OperationContext.StartActivity("upload"))
        await using (var database = new KnowledgeBaseDbContext(options))
        {
            var job = ProcessingJob.Queue("asset");
            database.Add(job);
            await database.SaveChangesAsync();
            original = job.DiagnosticContext!;
            Assert.Equal("upload", JobTraceContext.Read(original).Operation.UploadId);
            Assert.NotNull(JobTraceContext.Read(original).TraceParent);
        }
        await using var reloaded = new KnowledgeBaseDbContext(options);
        var persisted = await reloaded.ProcessingJobs.SingleAsync();
        Assert.Equal(original, persisted.DiagnosticContext);
        persisted.Status = ProcessingStatus.Running;
        await reloaded.SaveChangesAsync();
        using (OperationContext.Push(new(JobId: persisted.Id)))
        {
            persisted.Status = ProcessingStatus.Pending;
            await reloaded.SaveChangesAsync();
            Assert.Equal(original, persisted.DiagnosticContext);
        }
        persisted.Status = ProcessingStatus.Failed;
        persisted.Attempts = 3;
        await reloaded.SaveChangesAsync();
        using (OperationContext.Push(new(SessionId: "retry-session")))
        {
            persisted.Status = ProcessingStatus.Pending;
            persisted.Attempts = 0;
            await reloaded.SaveChangesAsync();
            Assert.Equal("retry-session", JobTraceContext.Read(persisted.DiagnosticContext).Operation.SessionId);
        }
    }

    [Fact]
    public void WorkerActivityContinuesStoredTraceAndContextIsRestored()
    {
        string traceId;
        string? parent;
        using (var request = OperationContext.StartActivity("request"))
        { traceId = request.TraceId.ToString(); parent = request.Id; }
        using (var worker = OperationContext.StartActivity("worker", parent))
            Assert.Equal(traceId, worker.TraceId.ToString());
        Assert.Null(Activity.Current);
        using (OperationContext.Push(new(UploadId: "first")))
        {
            using (OperationContext.Push(new(UploadId: "second"))) Assert.Equal("second", OperationContext.Current.UploadId);
            Assert.Equal("first", OperationContext.Current.UploadId);
        }
        Assert.Null(OperationContext.Current.UploadId);
    }

    [Fact]
    public void CollectorRejectsNestedPayloadsUnknownFieldsAndOversizedBatches()
    {
        var controller = new DiagnosticsController(NullLogger<DiagnosticsController>.Instance);
        BrowserDiagnostic Entry(string field, string json) => new("http.completed", DateTimeOffset.UtcNow, "information", new() { [field] = JsonDocument.Parse(json).RootElement.Clone() });
        Assert.IsType<BadRequestResult>(controller.Record(new("session", [Entry("message", "{}")])));
        Assert.IsType<BadRequestResult>(controller.Record(new("session", [Entry("cookie", "\"private\"")])));
        Assert.IsType<BadRequestResult>(controller.Record(new("session", Enumerable.Repeat(Entry("status", "200"), 51).ToList())));
        Assert.IsType<NoContentResult>(controller.Record(new("session", [Entry("status", "200")])));
    }

    [Fact]
    public void MigrationAndSnapshotMatchCurrentModelWithoutConnectingToDatabase()
    {
        using var database = new DesignTimeDbContextFactory().CreateDbContext([]);
        var migrations = database.GetService<IMigrationsAssembly>();
        Assert.Contains("20260923120000_AddJobDiagnosticContext", migrations.Migrations.Keys);
        var snapshot = database.GetService<IModelRuntimeInitializer>().Initialize(migrations.ModelSnapshot!.Model);
        var current = database.GetService<IDesignTimeModel>().Model;
        Assert.False(database.GetService<IMigrationsModelDiffer>().HasDifferences(snapshot.GetRelationalModel(), current.GetRelationalModel()));
    }
}
