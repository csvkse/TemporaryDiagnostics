namespace TemporaryDiagnostics.Core.Models;

/// <summary>
/// 内存日志条目。
/// </summary>
public sealed class DiagnosticLogItem
{
    public long Id { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string LogLevel { get; init; } = "Information";
    public string Category { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? Exception { get; init; }
}

/// <summary>
/// HTTP 请求诊断遥测记录。
/// </summary>
public sealed class DiagnosticHttpRequestItem
{
    public string TraceId { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string Method { get; init; } = "GET";
    public string Path { get; init; } = "/";
    public string? QueryString { get; init; }
    public int StatusCode { get; init; }
    public double DurationMs { get; init; }
    public string? ClientIp { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// 未捕获异常诊断记录。
/// </summary>
public sealed class DiagnosticExceptionItem
{
    public long Id { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string ExceptionType { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? StackTrace { get; init; }
    public string? RequestPath { get; init; }
}
