using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TemporaryDiagnostics.AspNetCore.Endpoints;
using TemporaryDiagnostics.AspNetCore.Middleware;
using TemporaryDiagnostics.AspNetCore.UI;
using TemporaryDiagnostics.Core.Configuration;

namespace TemporaryDiagnostics.AspNetCore.Extensions;

/// <summary>
/// 临时诊断中间件管道与端点挂载扩展。
/// </summary>
public static class TemporaryDiagnosticsApplicationBuilderExtensions
{
    /// <summary>
    /// 一键启用临时诊断中间件：自动配置 WebSockets、鉴权与遥测捕获中间件，并挂载所有诊断端点。
    /// </summary>
    public static WebApplication UseTemporaryDiagnostics(this WebApplication app)
    {
        // 1. 启用 WebSockets（用于交互终端）
        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(30)
        });

        // 2. 注入全局 HTTP 遥测与异常捕获中间件
        app.UseMiddleware<DiagnosticHttpCaptureMiddleware>();

        // 3. 注入诊断路径访问密钥鉴权中间件
        app.UseMiddleware<DiagnosticAuthMiddleware>();

        // 4. 挂载所有受控诊断端点
        app.MapTemporaryDiagnostics();

        return app;
    }

    /// <summary>
    /// 仅挂载临时诊断 Minimal API 端点与可视化 UI。
    /// </summary>
    public static IEndpointRouteBuilder MapTemporaryDiagnostics(this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<TemporaryDiagnosticsOptions>>().Value;
        var prefix = options.RoutePrefix;

        var diagGroup = endpoints.MapGroup(prefix);

        // 挂载单页面可视化 UI
        diagGroup.MapDiagnosticUi(prefix);

        // 挂载各领域 Minimal API
        diagGroup.MapAuthEndpoints();
        diagGroup.MapSystemEndpoints();
        diagGroup.MapFileEndpoints();
        diagGroup.MapLogEndpoints();

        if (options.EnableDynamicInvoke)
        {
            diagGroup.MapDynamicInvokeEndpoints();
        }

        if (options.EnableTerminal)
        {
            diagGroup.MapTerminalEndpoints();
        }

        if (options.EnableMcp)
        {
            diagGroup.MapMcpEndpoints();
        }

        if (options.EnableOpenApiDocs)
        {
            diagGroup.MapOpenApiEndpoints();
        }

        return endpoints;
    }
}
