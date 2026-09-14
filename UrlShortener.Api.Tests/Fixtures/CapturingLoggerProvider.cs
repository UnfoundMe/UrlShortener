using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace UrlShortener.Api.Tests.Fixtures;

public sealed record CapturedLogEntry(string Category, LogLevel Level, string Message);

// Task 9 (docs/url-shortener-tasks.md): a test-only ILoggerProvider that records every log
// event so integration tests can assert specific observability events fired, without a
// dependency on Serilog's own test sinks. Only reachable because Program.cs's UseSerilog call
// passes writeToProviders: true, which forwards events to providers registered in DI like this
// one (Serilog would otherwise be the sole consumer of ILogger<T> calls).
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLogEntry> _entries = new();

    public IReadOnlyCollection<CapturedLogEntry> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string categoryName, ConcurrentQueue<CapturedLogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            entries.Enqueue(new CapturedLogEntry(categoryName, logLevel, formatter(state, exception)));
        }
    }
}
