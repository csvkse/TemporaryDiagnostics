using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using TemporaryDiagnostics.Core.Configuration;
using TemporaryDiagnostics.Core.Contracts;
using TemporaryDiagnostics.Core.Models;

namespace TemporaryDiagnostics.AspNetCore.Middleware;

/// <summary>
/// 全局 HTTP 请求与未处理异常诊断遥测拦截中间件。
/// 参考 AspNetDebugDashboard 与 WatchDog.NET 机制，无损采集最近历史请求。
/// </summary>
public sealed class DiagnosticHttpCaptureMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IDiagnosticLogSink _sink;
    private readonly TemporaryDiagnosticsOptions _options;

    public DiagnosticHttpCaptureMiddleware(
        RequestDelegate next,
        IDiagnosticLogSink sink,
        IOptions<TemporaryDiagnosticsOptions> options)
    {
        _next = next;
        _sink = sink;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";

        // 避免记录自身高频诊断轮询请求
        var isDiagnosticSelf = path.StartsWith(_options.RoutePrefix, StringComparison.OrdinalIgnoreCase);

        var stopwatch = Stopwatch.StartNew();
        var clientIp = context.Connection.RemoteIpAddress?.ToString();

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _sink.RecordException(new DiagnosticExceptionItem
            {
                Timestamp = DateTime.UtcNow,
                ExceptionType = ex.GetType().Name,
                Message = ex.Message,
                StackTrace = ex.StackTrace,
                RequestPath = path
            });

            if (!isDiagnosticSelf)
            {
                _sink.RecordHttpRequest(new DiagnosticHttpRequestItem
                {
                    TraceId = context.TraceIdentifier,
                    Timestamp = DateTime.UtcNow,
                    Method = context.Request.Method,
                    Path = path,
                    QueryString = context.Request.QueryString.Value,
                    StatusCode = 500,
                    DurationMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
                    ClientIp = clientIp,
                    ErrorMessage = ex.Message
                });
            }

            throw;
        }

        stopwatch.Stop();

        if (!isDiagnosticSelf)
        {
            _sink.RecordHttpRequest(new DiagnosticHttpRequestItem
            {
                TraceId = context.TraceIdentifier,
                Timestamp = DateTime.UtcNow,
                Method = context.Request.Method,
                Path = path,
                QueryString = context.Request.QueryString.Value,
                StatusCode = context.Response.StatusCode,
                DurationMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
                ClientIp = clientIp
            });
        }
    }
}
