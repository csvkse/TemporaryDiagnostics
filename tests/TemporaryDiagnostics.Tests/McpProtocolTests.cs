using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TemporaryDiagnostics.Core.Configuration;
using TemporaryDiagnostics.Core.Models;
using TemporaryDiagnostics.Infrastructure.DynamicInvoke;
using TemporaryDiagnostics.Infrastructure.FileSystem;
using TemporaryDiagnostics.Infrastructure.Logging;
using TemporaryDiagnostics.Infrastructure.Mcp;
using TemporaryDiagnostics.Infrastructure.SystemInfo;
using TemporaryDiagnostics.SampleHost.Services;
using Xunit;

namespace TemporaryDiagnostics.Tests;

public class McpProtocolTests
{
    private readonly McpServerEngine _mcpEngine;
    private readonly IServiceProvider _serviceProvider;

    public McpProtocolTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOrderService, OrderService>();
        DiServiceInspector.SetServiceCollection(services);

        var options = Options.Create(new TemporaryDiagnosticsOptions
        {
            AccessKey = "SecretKey@12345"
        });

        var sysInfo = new CrossPlatformSystemInfoAdapter();
        var vitals = new RuntimeVitalsCollector();
        var logSink = new DiagnosticLogSink(options);
        var guard = new PathTraversalGuard(options);
        var fileService = new SecureFileService(guard, options);
        var dynamicInvoker = new DynamicInvokeCoordinator();

        _mcpEngine = new McpServerEngine(sysInfo, vitals, logSink, fileService, dynamicInvoker);
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Mcp_Initialize_ShouldReturnProtocolVersion()
    {
        var req = new McpRpcRequest
        {
            Id = 1,
            Method = "initialize"
        };

        var resp = await _mcpEngine.HandleRequestAsync(req, _serviceProvider);

        Assert.NotNull(resp.Result);
        var json = JsonSerializer.Serialize(resp.Result);
        Assert.Contains("2024-11-05", json);
        Assert.Contains("TemporaryDiagnostics-MCP-Server", json);
    }

    [Fact]
    public async Task Mcp_ToolsList_ShouldReturnAllTools()
    {
        var req = new McpRpcRequest
        {
            Id = 2,
            Method = "tools/list"
        };

        var resp = await _mcpEngine.HandleRequestAsync(req, _serviceProvider);

        Assert.NotNull(resp.Result);
        var json = JsonSerializer.Serialize(resp.Result);
        Assert.Contains("get_system_overview", json);
        Assert.Contains("get_runtime_vitals", json);
        Assert.Contains("query_recent_logs", json);
        Assert.Contains("list_directory_files", json);
        Assert.Contains("read_text_file", json);
        Assert.Contains("inspect_di_services", json);
        Assert.Contains("invoke_diagnostic_method", json);
        Assert.Contains("execute_diagnostic_command", json);
    }

    [Fact]
    public async Task Mcp_ToolsCall_GetRuntimeVitals_ShouldReturnValidText()
    {
        var req = new McpRpcRequest
        {
            Id = 3,
            Method = "tools/call",
            Params = JsonDocument.Parse("""{"name": "get_runtime_vitals", "arguments": {}}""").RootElement
        };

        var resp = await _mcpEngine.HandleRequestAsync(req, _serviceProvider);

        Assert.NotNull(resp.Result);
        Assert.Null(resp.Error);

        var toolResult = Assert.IsType<McpCallToolResult>(resp.Result);
        Assert.False(toolResult.IsError);
        Assert.NotEmpty(toolResult.Content);
        Assert.Contains("processId", toolResult.Content[0].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mcp_ToolsCall_InvokeDiagnosticMethod_ShouldWork()
    {
        var req = new McpRpcRequest
        {
            Id = 4,
            Method = "tools/call",
            Params = JsonDocument.Parse($$"""
            {
                "name": "invoke_diagnostic_method",
                "arguments": {
                    "service_type": "{{typeof(IOrderService).FullName}}",
                    "method_name": "GetOrderStatus",
                    "arguments": [ 1234 ]
                }
            }
            """).RootElement
        };

        var resp = await _mcpEngine.HandleRequestAsync(req, _serviceProvider);

        Assert.NotNull(resp.Result);
        var toolResult = Assert.IsType<McpCallToolResult>(resp.Result);
        Assert.False(toolResult.IsError);
        Assert.Contains("订单 #1234", toolResult.Content[0].Text);
    }
}
