using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using TemporaryDiagnostics.Core.Configuration;
using TemporaryDiagnostics.Core.Contracts;

namespace TemporaryDiagnostics.Infrastructure.Terminal;

/// <summary>
/// 跨平台 PTY 引擎，智能识别操作系统并选用最佳底层机制。
/// </summary>
public sealed class CrossPlatformPtyEngine : IPtyEngine
{
    private readonly TemporaryDiagnosticsOptions _options;
    private readonly PtySessionManager _sessionManager;

    public bool IsSupported => true;

    public CrossPlatformPtyEngine(IOptions<TemporaryDiagnosticsOptions> options, PtySessionManager sessionManager)
    {
        _options = options.Value;
        _sessionManager = sessionManager;
    }

    public async Task<IPtySession> StartSessionAsync(PtyStartOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var activeCount = _sessionManager.ListActiveSessions().Count;
        if (activeCount >= _options.MaxConcurrentTerminals)
        {
            throw new InvalidOperationException($"已达到最大并发终端会话限制 ({_options.MaxConcurrentTerminals})，请先关闭已有会话。");
        }

        var sessionId = Guid.NewGuid().ToString("N");
        var (shell, args) = ResolveShell(options.Executable ?? _options.DefaultShell, options.Arguments);

        // 1. Windows 原生 ConPTY 尝试
        if (OperatingSystem.IsWindows() && WindowsConPtyFactory.IsSupported)
        {
            var conPty = WindowsConPtyFactory.TryCreateSession(
                sessionId,
                shell,
                args,
                options.WorkingDirectory ?? AppContext.BaseDirectory,
                options.Columns,
                options.Rows,
                options.EnvironmentVariables);

            if (conPty != null)
            {
                _sessionManager.RegisterSession(conPty);
                return conPty;
            }
        }

        // 2. 跨平台标准流进程回退方案
        var startInfo = new ProcessStartInfo(shell)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            CreateNoWindow = true,
            WorkingDirectory = Directory.Exists(options.WorkingDirectory)
                ? options.WorkingDirectory
                : AppContext.BaseDirectory
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        startInfo.EnvironmentVariables["TERM"] = "xterm-256color";
        startInfo.EnvironmentVariables["COLORTERM"] = "truecolor";
        if (options.EnvironmentVariables != null)
        {
            foreach (var (k, v) in options.EnvironmentVariables)
            {
                startInfo.EnvironmentVariables[k] = v;
            }
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"无法启动 Shell 进程: {shell}");

        var session = new ProcessStreamPtySession(sessionId, process);
        _sessionManager.RegisterSession(session);
        return await Task.FromResult(session);
    }

    public IPtySession? GetSession(string sessionId) => _sessionManager.GetSession(sessionId);

    public Task<bool> CloseSessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
        _sessionManager.CloseSessionAsync(sessionId, cancellationToken);

    public IReadOnlyList<IPtySession> ListActiveSessions() => _sessionManager.ListActiveSessions();

    private static (string Executable, IReadOnlyList<string> Arguments) ResolveShell(string? requestedShell, IReadOnlyList<string>? requestedArgs)
    {
        if (!string.IsNullOrWhiteSpace(requestedShell))
        {
            return (requestedShell, requestedArgs ?? Array.Empty<string>());
        }

        if (OperatingSystem.IsWindows())
        {
            // 优先检测 powershell
            var systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
            var powershell = Path.Combine(systemRoot, @"System32\WindowsPowerShell\v1.0\powershell.exe");
            if (File.Exists(powershell))
            {
                return (powershell, new[] { "-NoLogo" });
            }

            var cmd = Path.Combine(systemRoot, @"System32\cmd.exe");
            return (cmd, Array.Empty<string>());
        }

        // Linux / macOS
        if (File.Exists("/bin/bash")) return ("/bin/bash", Array.Empty<string>());
        if (File.Exists("/bin/sh")) return ("/bin/sh", Array.Empty<string>());

        return ("sh", Array.Empty<string>());
    }
}
