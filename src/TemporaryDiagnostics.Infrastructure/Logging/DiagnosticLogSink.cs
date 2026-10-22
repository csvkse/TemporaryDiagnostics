using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TemporaryDiagnostics.Core.Configuration;
using TemporaryDiagnostics.Core.Contracts;
using TemporaryDiagnostics.Core.Models;

namespace TemporaryDiagnostics.Infrastructure.Logging;

/// <summary>
/// 诊断日志与 HTTP 遥测数据环形缓冲区存储槽（实现 IDiagnosticLogSink）。
/// </summary>
public sealed class DiagnosticLogSink : IDiagnosticLogSink
{
    private readonly ConcurrentRingBuffer<DiagnosticLogItem> _logs;
    private readonly ConcurrentRingBuffer<DiagnosticHttpRequestItem> _httpRequests;
    private readonly ConcurrentRingBuffer<DiagnosticExceptionItem> _exceptions;
    private long _logIdCounter;
    private long _exceptionIdCounter;

    public DiagnosticLogSink(IOptions<TemporaryDiagnosticsOptions> options)
    {
        _logs = new ConcurrentRingBuffer<DiagnosticLogItem>(options.Value.MaxLogRingBufferSize);
        _httpRequests = new ConcurrentRingBuffer<DiagnosticHttpRequestItem>(options.Value.MaxHttpHistorySize);
        _exceptions = new ConcurrentRingBuffer<DiagnosticExceptionItem>(options.Value.MaxLogRingBufferSize);
    }

    public void RecordLog(DiagnosticLogItem item)
    {
        var id = Interlocked.Increment(ref _logIdCounter);
        var entry = new DiagnosticLogItem
        {
            Id = id,
            Timestamp = item.Timestamp,
            LogLevel = item.LogLevel,
            Category = item.Category,
            Message = item.Message,
            Exception = item.Exception
        };
        _logs.Add(entry);
    }

    public void RecordHttpRequest(DiagnosticHttpRequestItem item)
    {
        _httpRequests.Add(item);
    }

    public void RecordException(DiagnosticExceptionItem item)
    {
        var id = Interlocked.Increment(ref _exceptionIdCounter);
        var entry = new DiagnosticExceptionItem
        {
            Id = id,
            Timestamp = item.Timestamp,
            ExceptionType = item.ExceptionType,
            Message = item.Message,
            StackTrace = item.StackTrace,
            RequestPath = item.RequestPath
        };
        _exceptions.Add(entry);
    }

    public IReadOnlyList<DiagnosticLogItem> GetRecentLogs(int limit = 100) => _logs.ToList(limit);

    public IReadOnlyList<DiagnosticHttpRequestItem> GetRecentHttpRequests(int limit = 100) => _httpRequests.ToList(limit);

    public IReadOnlyList<DiagnosticExceptionItem> GetRecentExceptions(int limit = 100) => _exceptions.ToList(limit);

    public void Clear()
    {
        _logs.Clear();
        _httpRequests.Clear();
        _exceptions.Clear();
    }
}

/// <summary>
/// 将 ASP.NET Core 运行日志自动引入诊断环形缓冲区的 LoggerProvider。
/// </summary>
public sealed class DiagnosticLoggerProvider : ILoggerProvider
{
    private readonly IDiagnosticLogSink _sink;

    public DiagnosticLoggerProvider(IDiagnosticLogSink sink)
    {
        _sink = sink;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new DiagnosticLogger(categoryName, _sink);
    }

    public void Dispose() { }

    private sealed class DiagnosticLogger : ILogger
    {
        private readonly string _category;
        private readonly IDiagnosticLogSink _sink;

        public DiagnosticLogger(string category, IDiagnosticLogSink sink)
        {
            _category = category;
            _sink = sink;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            // 过滤自身诊断中间件日志避免死循环
            if (_category.StartsWith("TemporaryDiagnostics", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var message = formatter(state, exception);
            _sink.RecordLog(new DiagnosticLogItem
            {
                Timestamp = DateTime.UtcNow,
                LogLevel = logLevel.ToString(),
                Category = _category,
                Message = message,
                Exception = exception?.ToString()
            });

            if (exception != null)
            {
                _sink.RecordException(new DiagnosticExceptionItem
                {
                    Timestamp = DateTime.UtcNow,
                    ExceptionType = exception.GetType().Name,
                    Message = exception.Message,
                    StackTrace = exception.StackTrace
                });
            }
        }
    }
}
