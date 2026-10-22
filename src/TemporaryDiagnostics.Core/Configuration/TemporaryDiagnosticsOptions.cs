namespace TemporaryDiagnostics.Core.Configuration;

/// <summary>
/// 临时诊断中间件配置选项。
/// 核心原则：一键启动，仅 AccessKey 为必填；其它高级选项具备工业级安全默认值。
/// </summary>
public sealed class TemporaryDiagnosticsOptions
{
    private string _accessKey = string.Empty;
    private string _routePrefix = "/__diag";

    /// <summary>
    /// 临时诊断访问密钥（必填）。
    /// 必须具有足够的熵（默认要求最少 8 个字符）。
    /// </summary>
    public string AccessKey
    {
        get => _accessKey;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("诊断访问密钥 (AccessKey) 不能为空。", nameof(value));
            }
            if (value.Trim().Length < 6)
            {
                throw new ArgumentException("为了安全起见，诊断访问密钥长度不能少于 6 位。", nameof(value));
            }
            _accessKey = value.Trim();
        }
    }

    /// <summary>
    /// 诊断路由统一前缀（可选，默认为 "/__diag"）。
    /// </summary>
    public string RoutePrefix
    {
        get => _routePrefix;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                _routePrefix = "/__diag";
                return;
            }
            var trimmed = value.Trim();
            if (!trimmed.StartsWith('/'))
            {
                trimmed = "/" + trimmed;
            }
            _routePrefix = trimmed.TrimEnd('/');
        }
    }

    /// <summary>
    /// 会话无操作过期时长（分钟），默认 30 分钟。超时后自动断开并不再响应受保护操作。
    /// </summary>
    public int SessionTimeoutMinutes { get; set; } = 30;

    /// <summary>
    /// 是否开启 Web 交互式终端。默认为 true。
    /// </summary>
    public bool EnableTerminal { get; set; } = true;

    /// <summary>
    /// 最大并发终端会话数。默认 2 个，防止后台进程风暴。
    /// </summary>
    public int MaxConcurrentTerminals { get; set; } = 2;

    /// <summary>
    /// 终端使用的 Shell 可执行程序。为 null 时自动依据操作系统探测（Windows: powershell/cmd, Linux: bash/sh）。
    /// </summary>
    public string? DefaultShell { get; set; }

    /// <summary>
    /// 是否开启基于反射与 IL Emit 的动态调用与 DI 容器探查功能。默认为 true。
    /// </summary>
    public bool EnableDynamicInvoke { get; set; } = true;

    /// <summary>
    /// 文件管理器的物理根目录白名单。默认为应用所在目录和日志目录。
    /// </summary>
    public List<string> AllowedFileRoots { get; set; } = new()
    {
        AppContext.BaseDirectory,
        Path.Combine(AppContext.BaseDirectory, "logs"),
        Path.Combine(Directory.GetCurrentDirectory(), "logs")
    };

    /// <summary>
    /// 是否在诊断模式下开放查看所有可用驱动器（Windows 下 C/D 盘，Linux 下 /）。
    /// 默认为 true，方便开发与紧急运维排查；若需严格限定目录，可置为 false 并通过 AllowedFileRoots 白名单配置。
    /// </summary>
    public bool AllowAllDrivesInDiagnostics { get; set; } = true;

    /// <summary>
    /// 允许在线查看和保存的单文本文件最大字节数，默认 2MB。超过则提示下载。
    /// </summary>
    public long MaxTextFileSize { get; set; } = 2 * 1024 * 1024;

    /// <summary>
    /// 内存日志环形缓冲区最大容量，默认 500 条。
    /// </summary>
    public int MaxLogRingBufferSize { get; set; } = 500;

    /// <summary>
    /// 内存中保留的最近 HTTP 历史请求数，默认 200 条。
    /// </summary>
    public int MaxHttpHistorySize { get; set; } = 200;

    /// <summary>
    /// 是否开启 MCP (Model Context Protocol) 协议接口。默认为 true。
    /// </summary>
    public bool EnableMcp { get; set; } = true;

    /// <summary>
    /// 是否开启 OpenAPI 规范与 Scalar/Swagger UI 文档页面。默认为 true。
    /// </summary>
    public bool EnableOpenApiDocs { get; set; } = true;
}
