using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace KnowledgeBase.Core.Observability;

public sealed class DatabaseTimingInterceptor(ILogger<DatabaseTimingInterceptor> logger) : DbCommandInterceptor
{
    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) =>
        logger.LogError(eventData.Exception, "Database {CommandKind} failed in {DurationMs} ms", eventData.ExecuteMethod, eventData.Duration.TotalMilliseconds);

    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    { CommandFailed(command, eventData); return Task.CompletedTask; }

    private void Completed(CommandExecutedEventData data) => logger.Log(
        data.Duration.TotalMilliseconds >= 500 ? LogLevel.Warning : LogLevel.Debug,
        "Database {CommandKind} completed in {DurationMs} ms", data.ExecuteMethod, data.Duration.TotalMilliseconds);

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    { Completed(eventData); return result; }
    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    { Completed(eventData); return ValueTask.FromResult(result); }
    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    { Completed(eventData); return result; }
    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    { Completed(eventData); return ValueTask.FromResult(result); }
    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    { Completed(eventData); return result; }
    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
    { Completed(eventData); return ValueTask.FromResult(result); }
}
