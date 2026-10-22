using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TemporaryDiagnostics.AspNetCore.Security;
using TemporaryDiagnostics.Core.Configuration;
using TemporaryDiagnostics.Core.Contracts;
using TemporaryDiagnostics.Infrastructure.DynamicInvoke;
using TemporaryDiagnostics.Infrastructure.FileSystem;
using TemporaryDiagnostics.Infrastructure.Logging;
using TemporaryDiagnostics.Infrastructure.Mcp;
using TemporaryDiagnostics.Infrastructure.SystemInfo;
using TemporaryDiagnostics.Infrastructure.Terminal;

namespace TemporaryDiagnostics.AspNetCore.Extensions;

/// <summary>
/// 临时诊断中间件服务注册扩展。
/// </summary>
public static class TemporaryDiagnosticsServiceCollectionExtensions
{
    public static IServiceCollection AddTemporaryDiagnostics(
        this IServiceCollection services,
        Action<TemporaryDiagnosticsOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        // 捕获已注册的 IServiceCollection 实例，供 DI 容器反射探查器使用
        DiServiceInspector.SetServiceCollection(services);

        var options = new TemporaryDiagnosticsOptions();
        configure(options);

        // 强制校验访问密钥
        if (string.IsNullOrWhiteSpace(options.AccessKey))
        {
            throw new ArgumentException("启用 TemporaryDiagnostics 中间件必须提供 AccessKey 访问密钥。", nameof(configure));
        }

        services.Configure(configure);

        // 核心基础设施与服务单例注册
        services.TryAddSingleton<DiagnosticSessionStore>();
        services.TryAddSingleton<ISystemInfoAdapter, CrossPlatformSystemInfoAdapter>();
        services.TryAddSingleton<IRuntimeVitalsCollector, RuntimeVitalsCollector>();
        services.TryAddSingleton<PathTraversalGuard>();
        services.TryAddSingleton<ISecureFileService, SecureFileService>();
        services.TryAddSingleton<PtySessionManager>();
        services.TryAddSingleton<IPtyEngine, CrossPlatformPtyEngine>();
        services.TryAddSingleton<IDynamicInvoker, DynamicInvokeCoordinator>();
        services.TryAddSingleton<IDiagnosticLogSink, DiagnosticLogSink>();
        services.TryAddSingleton<IMcpServerEngine, McpServerEngine>();

        // 注册全局日志收集器
        services.AddSingleton<ILoggerProvider, DiagnosticLoggerProvider>();

        return services;
    }
}
