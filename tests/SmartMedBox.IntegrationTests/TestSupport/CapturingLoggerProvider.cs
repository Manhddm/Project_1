using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace SmartMedBox.IntegrationTests.TestSupport;

public sealed record CapturedLog(string Category, string Message, IReadOnlyList<KeyValuePair<string, object?>> ScopeValues)
{
    public string AllText => Message + " " + string.Join(" ", ScopeValues.Select(kv => $"{kv.Key}={kv.Value}"));
}

/// <summary>Ghi lại message + giá trị scope của mọi log để test kiểm tra correlation/redaction.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public ConcurrentQueue<CapturedLog> Logs { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(CapturingLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => provider._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = new List<KeyValuePair<string, object?>>();
            provider._scopes.ForEachScope((scope, list) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
                {
                    list.AddRange(pairs);
                }
                else
                {
                    list.Add(new KeyValuePair<string, object?>("Scope", scope?.ToString()));
                }
            }, values);

            provider.Logs.Enqueue(new CapturedLog(category, formatter(state, exception), values));
        }
    }
}
