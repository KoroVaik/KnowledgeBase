using System.Diagnostics;
using System.Reflection;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Sinks.Async;

namespace KnowledgeBase.Core.Observability;

public static class LoggingSetup
{
    public static Logger Bootstrap() => new LoggerConfiguration()
        .WriteTo.Sink(new RedactingSink(new LoggerConfiguration().WriteTo.Console(new CompactJsonFormatter()).CreateLogger()))
        .CreateLogger();
    public static void AddObservability(this IHostApplicationBuilder builder, string service)
    {
        var settings = builder.Configuration.GetSection("Observability");
        var level = Enum.TryParse<LogEventLevel>(settings["MinimumLevel"], true, out var parsed) ? parsed : LogEventLevel.Information;
        var output = new LoggerConfiguration().MinimumLevel.Verbose()
            .WriteTo.Console(new CompactJsonFormatter());
        var directory = settings["LogDirectory"];
        if (!string.IsNullOrWhiteSpace(directory))
        {
            output.WriteTo.Logger(log => log.MinimumLevel.Verbose().Filter.ByIncludingOnly(e => e.Level >= LogEventLevel.Information)
                .WriteTo.File(new CompactJsonFormatter(), Path.Combine(directory, service + "-.jsonl"),
                    rollingInterval: RollingInterval.Day, fileSizeLimitBytes: 20_000_000,
                    rollOnFileSizeLimit: true, retainedFileCountLimit: 7, retainedFileTimeLimit: TimeSpan.FromDays(7)));
            output.WriteTo.Logger(log => log.MinimumLevel.Verbose().Filter.ByIncludingOnly(e => e.Level < LogEventLevel.Information)
                .WriteTo.File(new CompactJsonFormatter(), Path.Combine(directory, service + "-debug-.jsonl"),
                    rollingInterval: RollingInterval.Hour, fileSizeLimitBytes: 10_000_000,
                    rollOnFileSizeLimit: true, retainedFileCountLimit: 24, retainedFileTimeLimit: TimeSpan.FromDays(1)));
        }
        if (Uri.TryCreate(settings["SeqUrl"], UriKind.Absolute, out var seqUrl))
            output.WriteTo.Seq(seqUrl.AbsoluteUri, apiKey: settings["SeqApiKey"], queueSizeLimit: 5000);

        Log.Logger = new LoggerConfiguration().MinimumLevel.Is(level)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Fatal)
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .Enrich.FromLogContext().Enrich.With(new OperationEnricher())
            .Enrich.WithProperty("Service", service)
            .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName)
            .Enrich.WithProperty("Release", settings["Release"] ?? Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown")
            .WriteTo.Async(sink => sink.Sink(new RedactingSink(output.CreateLogger())), bufferSize: 10000,
                blockWhenFull: false, monitor: new DroppedLogMonitor())
            .CreateLogger();
        builder.Services.AddSerilog(Log.Logger, dispose: true);
        builder.Services.AddSingleton<JobContextInterceptor>();
        builder.Services.AddSingleton<DatabaseTimingInterceptor>();
        builder.Services.AddTransient<DependencyLoggingHandler>();
        builder.Services.ConfigureHttpClientDefaults(client => client.AddHttpMessageHandler<DependencyLoggingHandler>());
    }

    private sealed class OperationEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory factory)
        {
            var context = OperationContext.Current;
            Add("TraceId", Activity.Current?.TraceId.ToString());
            Add("SpanId", Activity.Current?.SpanId.ToString());
            Add("SessionId", context.SessionId);
            Add("UploadBatchId", context.UploadBatchId);
            Add("UploadId", context.UploadId);
            Add("CausationId", context.CausationId);
            Add("JobId", context.JobId);
            void Add(string name, string? value)
            {
                if (value is not null) logEvent.AddPropertyIfAbsent(factory.CreateProperty(name, value));
            }
        }
    }

    private sealed class DroppedLogMonitor : IAsyncLogEventSinkMonitor
    {
        private Timer? timer;
        private long previous;
        public void StartMonitoring(IAsyncLogEventSinkInspector inspector) => timer = new Timer(_ =>
        {
            var count = inspector.DroppedMessagesCount;
            if (count > previous)
                Console.Error.WriteLine($"{{\"Event\":\"logging.dropped\",\"Count\":{count - previous}}}");
            previous = count;
        }, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
        public void StopMonitoring(IAsyncLogEventSinkInspector inspector) => timer?.Dispose();
    }
}
