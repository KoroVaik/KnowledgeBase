using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace KnowledgeBase.Core.Observability;

public static partial class LogRedaction
{
    public static string Text(string value)
    {
        value = value.Length > 8000 ? value[..8000] : value;
        value = UrlQuery().Replace(value, "$1?[redacted]");
        value = Credentials().Replace(value, "$1=[redacted]");
        return Bearer().Replace(value, "Bearer [redacted]");
    }

    [GeneratedRegex(@"(https?://[^\s?'""<>]+)\?[^\s'""<>]*", RegexOptions.IgnoreCase)]
    private static partial Regex UrlQuery();

    [GeneratedRegex("""\b(password|pwd|authorization|cookie|set-cookie|x-ingest-token|api[-_]?key|secret|token|signature)["']?\s*[:=]\s*(?:"[^"]*"|'[^']*'|[^\s;,]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex Credentials();

    [GeneratedRegex(@"Bearer\s+[^\s,'"";]+", RegexOptions.IgnoreCase)]
    private static partial Regex Bearer();

    public static LogEventProperty NamedProperty(string name, LogEventPropertyValue value) =>
        new(name, SensitiveName().IsMatch(name) ? new ScalarValue("[redacted]") : Property(value));

    [GeneratedRegex(@"password|authorization|cookie|token|secret|apikey|api_key|api-key|signature|connectionstring", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveName();

    public static LogEventPropertyValue Property(LogEventPropertyValue value) => value switch
    {
        ScalarValue { Value: string text } => new ScalarValue(Text(text)),
        SequenceValue sequence => new SequenceValue(sequence.Elements.Select(Property)),
        StructureValue structure => new StructureValue(structure.Properties.Select(p => NamedProperty(p.Name, p.Value)), structure.TypeTag),
        DictionaryValue dictionary => new DictionaryValue(dictionary.Elements.Select(p => new KeyValuePair<ScalarValue, LogEventPropertyValue>((ScalarValue)Property(p.Key), NamedProperty(p.Key.Value?.ToString() ?? "", p.Value).Value))),
        _ => value,
    };
}

internal sealed class RedactingSink(Serilog.Core.Logger output) : ILogEventSink, IDisposable
{
    public void Emit(LogEvent logEvent)
    {
        var properties = logEvent.Properties.Select(p => LogRedaction.NamedProperty(p.Key, p.Value));
        var exception = logEvent.Exception is null ? null : new RedactedException(logEvent.Exception);
        output.Write(new LogEvent(logEvent.Timestamp, logEvent.Level, exception, logEvent.MessageTemplate, properties));
    }

    public void Dispose() => output.Dispose();

    private sealed class RedactedException(Exception original) : Exception(LogRedaction.Text(original.Message))
    {
        public override string ToString() => LogRedaction.Text(original.ToString());
    }
}
