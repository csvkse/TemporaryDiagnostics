using System.Diagnostics;
using System.Text.Json;
using TemporaryDiagnostics.Core.Contracts;
using TemporaryDiagnostics.Core.Models;

namespace TemporaryDiagnostics.Infrastructure.Mcp;

/// <summary>
/// Model Context Protocol (MCP) 原生 JSON-RPC 2.0 分发引擎（实现 IMcpServerEngine）。
/// 兼容 Anthropic MCP 协议规范 (2024-11-05)。
/// </summary>
public sealed class McpServerEngine : IMcpServerEngine
{
    private readonly ISystemInfoAdapter _systemInfo;
    private readonly IRuntimeVitalsCollector _vitals;
    private readonly IDiagnosticLogSink _logSink;
    private readonly ISecureFileService _fileService;
    private readonly IDynamicInvoker _dynamicInvoker;

    public McpServerEngine(
        ISystemInfoAdapter systemInfo,
        IRuntimeVitalsCollector vitals,
        IDiagnosticLogSink logSink,
        ISecureFileService fileService,
        IDynamicInvoker dynamicInvoker)
    {
        _systemInfo = systemInfo;
        _vitals = vitals;
        _logSink = logSink;
        _fileService = fileService;
        _dynamicInvoker = dynamicInvoker;
    }

    public async Task<McpRpcResponse> HandleRequestAsync(
        McpRpcRequest request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return request.Method switch
            {
                "initialize" => HandleInitialize(request),
                "ping" => McpRpcResponse.Success(request.Id, new { }),
                "tools/list" => HandleToolsList(request),
                "tools/call" => await HandleToolsCallAsync(request, serviceProvider, cancellationToken),
                _ => McpRpcResponse.Fail(request.Id, -32601, $"Method not found: {request.Method}")
            };
        }
        catch (Exception ex)
        {
            return McpRpcResponse.Fail(request.Id, -32603, $"Internal server error: {ex.Message}");
        }
    }

    private static McpRpcResponse HandleInitialize(McpRpcRequest request)
    {
        var initResult = new
        {
            protocolVersion = "2024-11-05",
            capabilities = new
            {
                tools = new { }
            },
            serverInfo = new
            {
                name = "TemporaryDiagnostics-MCP-Server",
                version = "1.0.0"
            }
        };

        return McpRpcResponse.Success(request.Id, initResult);
    }

    private McpRpcResponse HandleToolsList(McpRpcRequest request)
    {
        var tools = GetToolDefinitions();
        return McpRpcResponse.Success(request.Id, new { tools });
    }

    private async Task<McpRpcResponse> HandleToolsCallAsync(
        McpRpcRequest request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        if (request.Params == null || !request.Params.Value.TryGetProperty("name", out var nameProp))
        {
            return McpRpcResponse.Fail(request.Id, -32602, "Missing tool name in params");
        }

        var toolName = nameProp.GetString();
        var arguments = request.Params.Value.TryGetProperty("arguments", out var argsProp)
            ? argsProp
            : default;

        try
        {
            McpCallToolResult callResult = toolName switch
            {
                "get_system_overview" => await ExecGetSystemOverviewAsync(cancellationToken),
                "get_runtime_vitals" => ExecGetRuntimeVitals(),
                "query_recent_logs" => ExecQueryRecentLogs(arguments),
                "list_directory_files" => await ExecListDirectoryFilesAsync(arguments, cancellationToken),
                "read_text_file" => await ExecReadTextFileAsync(arguments, cancellationToken),
                "inspect_di_services" => ExecInspectDiServices(serviceProvider, arguments),
                "invoke_diagnostic_method" => await ExecInvokeDiagnosticMethodAsync(serviceProvider, arguments, cancellationToken),
                "execute_diagnostic_command" => await ExecDiagnosticCommandAsync(arguments, cancellationToken),
                _ => McpCallToolResult.Text($"Unknown tool: {toolName}", isError: true)
            };

            return McpRpcResponse.Success(request.Id, callResult);
        }
        catch (Exception ex)
        {
            return McpRpcResponse.Success(request.Id, McpCallToolResult.Text($"Tool execution failed: {ex.Message}", isError: true));
        }
    }

    private async Task<McpCallToolResult> ExecGetSystemOverviewAsync(CancellationToken ct)
    {
        var overview = await _systemInfo.GetSystemOverviewAsync(ct);
        return McpCallToolResult.Text(JsonSerializer.Serialize(overview, JsonOptions));
    }

    private McpCallToolResult ExecGetRuntimeVitals()
    {
        var vitals = _vitals.GetCurrentVitals();
        return McpCallToolResult.Text(JsonSerializer.Serialize(vitals, JsonOptions));
    }

    private McpCallToolResult ExecQueryRecentLogs(JsonElement args)
    {
        var category = "all";
        var limit = 50;

        if (args.ValueKind == JsonValueKind.Object)
        {
            if (args.TryGetProperty("category", out var catProp) && catProp.GetString() is { } c) category = c;
            if (args.TryGetProperty("limit", out var limProp) && limProp.TryGetInt32(out var l)) limit = Math.Clamp(l, 1, 200);
        }

        object resultData = category.ToLowerInvariant() switch
        {
            "requests" => _logSink.GetRecentHttpRequests(limit),
            "exceptions" => _logSink.GetRecentExceptions(limit),
            "logs" => _logSink.GetRecentLogs(limit),
            _ => new
            {
                logs = _logSink.GetRecentLogs(limit),
                requests = _logSink.GetRecentHttpRequests(limit),
                exceptions = _logSink.GetRecentExceptions(limit)
            }
        };

        return McpCallToolResult.Text(JsonSerializer.Serialize(resultData, JsonOptions));
    }

    private async Task<McpCallToolResult> ExecListDirectoryFilesAsync(JsonElement args, CancellationToken ct)
    {
        var path = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("path", out var pathProp)
            ? pathProp.GetString()
            : "/";

        var listResult = await _fileService.ListDirectoryAsync(path, ct);
        if (!listResult.IsSuccess)
        {
            return McpCallToolResult.Text($"Failed to list files: {listResult.Error}", isError: true);
        }

        return McpCallToolResult.Text(JsonSerializer.Serialize(listResult.Data, JsonOptions));
    }

    private async Task<McpCallToolResult> ExecReadTextFileAsync(JsonElement args, CancellationToken ct)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("path", out var pathProp))
        {
            return McpCallToolResult.Text("Missing path argument", isError: true);
        }

        var path = pathProp.GetString()!;
        var readResult = await _fileService.ReadFileTextAsync(path, ct);
        if (!readResult.IsSuccess)
        {
            return McpCallToolResult.Text($"Failed to read file: {readResult.Error}", isError: true);
        }

        return McpCallToolResult.Text(JsonSerializer.Serialize(readResult.Data, JsonOptions));
    }

    private McpCallToolResult ExecInspectDiServices(IServiceProvider sp, JsonElement args)
    {
        var filter = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("filter", out var fProp)
            ? fProp.GetString()
            : null;

        var services = _dynamicInvoker.GetRegisteredServices(sp);
        if (!string.IsNullOrWhiteSpace(filter))
        {
            services = services.Where(s =>
                s.ServiceType.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                s.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return McpCallToolResult.Text(JsonSerializer.Serialize(services, JsonOptions));
    }

    private async Task<McpCallToolResult> ExecInvokeDiagnosticMethodAsync(IServiceProvider sp, JsonElement args, CancellationToken ct)
    {
        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty("service_type", out var typeProp) ||
            !args.TryGetProperty("method_name", out var methodProp))
        {
            return McpCallToolResult.Text("Missing service_type or method_name", isError: true);
        }

        var serviceType = typeProp.GetString()!;
        var methodName = methodProp.GetString()!;

        var paramList = new List<object?>();
        if (args.TryGetProperty("arguments", out var argListProp) && argListProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in argListProp.EnumerateArray())
            {
                paramList.Add(item.Clone());
            }
        }

        var invokeReq = new DynamicInvokeRequest
        {
            ServiceTypeName = serviceType,
            MethodName = methodName,
            Parameters = paramList,
            UseFastEmit = true
        };

        var invokeResp = await _dynamicInvoker.InvokeAsync(invokeReq, sp, ct);
        return McpCallToolResult.Text(JsonSerializer.Serialize(invokeResp, JsonOptions), isError: !invokeResp.Success);
    }

    private static async Task<McpCallToolResult> ExecDiagnosticCommandAsync(JsonElement args, CancellationToken ct)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("command", out var cmdProp))
        {
            return McpCallToolResult.Text("Missing command argument", isError: true);
        }

        var command = cmdProp.GetString()!;
        var timeoutSeconds = args.TryGetProperty("timeout_seconds", out var tProp) && tProp.TryGetInt32(out var t)
            ? Math.Clamp(t, 1, 60)
            : 30;

        using var process = new Process();
        if (OperatingSystem.IsWindows())
        {
            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.Arguments = $"/c {command}";
        }
        else
        {
            process.StartInfo.FileName = "sh";
            process.StartInfo.Arguments = $"-c \"{command.Replace("\"", "\\\"")}\"";
        }

        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;

        process.Start();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
            var stdout = await process.StandardOutput.ReadToEndAsync(ct);
            var stderr = await process.StandardError.ReadToEndAsync(ct);

            var output = string.IsNullOrWhiteSpace(stderr)
                ? stdout
                : $"[STDOUT]\n{stdout}\n\n[STDERR]\n{stderr}";

            return McpCallToolResult.Text(output, isError: process.ExitCode != 0);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            return McpCallToolResult.Text($"Command execution timed out after {timeoutSeconds}s", isError: true);
        }
    }

    public IReadOnlyList<McpToolDefinition> GetToolDefinitions() => new List<McpToolDefinition>
    {
        new()
        {
            Name = "get_system_overview",
            Description = "获取宿主系统硬件、操作系统、CPU 核心数、物理内存与磁盘挂载分区概览。",
            InputSchema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement
        },
        new()
        {
            Name = "get_runtime_vitals",
            Description = "获取 .NET 运行时健康体征（GC 分代/堆大小/暂停率、ThreadPool 线程、进程工作集与 CPU%）。",
            InputSchema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement
        },
        new()
        {
            Name = "query_recent_logs",
            Description = "查询内存环形缓冲区中的最近应用日志、HTTP 历史请求与未捕获异常堆栈。",
            InputSchema = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "category": { "type": "string", "enum": ["all", "logs", "requests", "exceptions"], "description": "检索分类" },
                    "limit": { "type": "integer", "description": "最大获取条数，默认 50" }
                }
            }
            """).RootElement
        },
        new()
        {
            Name = "list_directory_files",
            Description = "浏览白名单受控目录下的文件列表（支持目录树递归浏览）。",
            InputSchema = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "path": { "type": "string", "description": "目标目录绝对或相对路径" }
                },
                "required": ["path"]
            }
            """).RootElement
        },
        new()
        {
            Name = "read_text_file",
            Description = "只读查看白名单路径下的文本文件内容（最大 2MB 预览）。",
            InputSchema = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "path": { "type": "string", "description": "文件物理绝对路径" }
                },
                "required": ["path"]
            }
            """).RootElement
        },
        new()
        {
            Name = "inspect_di_services",
            Description = "探查当前应用 IServiceProvider 中注册的依赖注入服务、生命周期和可用公开方法。",
            InputSchema = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "filter": { "type": "string", "description": "服务名称或接口名称关键字过滤" }
                }
            }
            """).RootElement
        },
        new()
        {
            Name = "invoke_diagnostic_method",
            Description = "通过 IL Emit 高速编译器动态调用 DI 注册服务的公共方法并获取 JSON 结果。",
            InputSchema = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "service_type": { "type": "string", "description": "服务接口或类名称" },
                    "method_name": { "type": "string", "description": "待调用的公共方法名称" },
                    "arguments": { "type": "array", "description": "方法调用入参列表" }
                },
                "required": ["service_type", "method_name"]
            }
            """).RootElement
        },
        new()
        {
            Name = "execute_diagnostic_command",
            Description = "在受限沙箱中执行单次命令行排查指令并获取输出。",
            InputSchema = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "command": { "type": "string", "description": "执行指令文本" },
                    "timeout_seconds": { "type": "integer", "description": "执行超时秒数，默认 30" }
                },
                "required": ["command"]
            }
            """).RootElement
        }
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
