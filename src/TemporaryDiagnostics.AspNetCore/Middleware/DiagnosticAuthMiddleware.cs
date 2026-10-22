using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using TemporaryDiagnostics.AspNetCore.Security;
using TemporaryDiagnostics.Core.Configuration;

namespace TemporaryDiagnostics.AspNetCore.Middleware;

/// <summary>
/// 诊断中间件统一身份鉴权与安全门禁。
/// 保护前缀下所有受控 API、WebSocket 与 MCP 端点。
/// </summary>
public sealed class DiagnosticAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TemporaryDiagnosticsOptions _options;
    private readonly DiagnosticSessionStore _sessionStore;

    public DiagnosticAuthMiddleware(
        RequestDelegate next,
        IOptions<TemporaryDiagnosticsOptions> options,
        DiagnosticSessionStore sessionStore)
    {
        _next = next;
        _options = options.Value;
        _sessionStore = sessionStore;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var prefix = _options.RoutePrefix;

        // 非诊断路由，直接放行
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var subPath = path.Length > prefix.Length ? path.Substring(prefix.Length) : "/";

        // 白名单放行路径：登录接口、静态 UI 主页与前端资源（前端自行判断登录态展示登录框）
        if (subPath.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase) ||
            subPath.Equals("/api/auth/status", StringComparison.OrdinalIgnoreCase) ||
            subPath.Equals("/") ||
            subPath.Equals("/index.html", StringComparison.OrdinalIgnoreCase) ||
            subPath.StartsWith("/static", StringComparison.OrdinalIgnoreCase) ||
            subPath.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

        // 提取凭证：Header / Cookie / Query
        var tokenOrKey = ExtractCredential(context);

        if (_sessionStore.ValidateKeyOrToken(tokenOrKey, clientIp))
        {
            await _next(context);
            return;
        }

        // 鉴权未通过
        context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync("{\"isSuccess\":false,\"error\":\"未授权访问或访问凭证无效\",\"statusCode\":401}");
    }

    private static string? ExtractCredential(HttpContext context)
    {
        // 1. X-Diag-Key 请求头
        if (context.Request.Headers.TryGetValue("X-Diag-Key", out var headerKey) && !string.IsNullOrWhiteSpace(headerKey))
        {
            return headerKey.ToString();
        }

        // 2. Authorization: Bearer <token/key>
        if (context.Request.Headers.TryGetValue("Authorization", out var authHeader) && !string.IsNullOrWhiteSpace(authHeader))
        {
            var headerVal = authHeader.ToString();
            if (headerVal.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return headerVal.Substring("Bearer ".Length).Trim();
            }
            return headerVal.Trim();
        }

        // 3. Cookie
        if (context.Request.Cookies.TryGetValue("__diag_token", out var cookieToken) && !string.IsNullOrWhiteSpace(cookieToken))
        {
            return cookieToken;
        }

        // 4. Query string (例如 WebSocket 握手 /terminal/ws?token=...)
        if (context.Request.Query.TryGetValue("token", out var queryToken) && !string.IsNullOrWhiteSpace(queryToken))
        {
            return queryToken.ToString();
        }

        if (context.Request.Query.TryGetValue("key", out var queryKey) && !string.IsNullOrWhiteSpace(queryKey))
        {
            return queryKey.ToString();
        }

        return null;
    }
}
