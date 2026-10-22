using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TemporaryDiagnostics.Core.Contracts;
using TemporaryDiagnostics.Core.Models;

namespace TemporaryDiagnostics.AspNetCore.Endpoints;

public static class McpEndpoints
{
    private static readonly ConcurrentDictionary<string, Channel<string>> SseSessions = new();

    public static RouteGroupBuilder MapMcpEndpoints(this RouteGroupBuilder group)
    {
        // 1. 直接 Streamable HTTP JSON-RPC 2.0 端点: POST /mcp
        group.MapPost("/mcp", async (
            HttpContext context,
            IMcpServerEngine engine,
            IServiceProvider sp,
            CancellationToken ct) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync(ct);
            if (string.IsNullOrWhiteSpace(body))
            {
                return Results.BadRequest(McpRpcResponse.Fail(null, -32700, "Empty request body"));
            }

            McpRpcRequest? rpcRequest;
            try
            {
                rpcRequest = JsonSerializer.Deserialize<McpRpcRequest>(body);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(McpRpcResponse.Fail(null, -32700, $"Parse error: {ex.Message}"));
            }

            if (rpcRequest == null)
            {
                return Results.BadRequest(McpRpcResponse.Fail(null, -32600, "Invalid Request"));
            }

            var response = await engine.HandleRequestAsync(rpcRequest, sp, ct);
            return Results.Json(response);
        });

        // 2. 工具列表便捷接口: GET /mcp/tools
        group.MapGet("/mcp/tools", (IMcpServerEngine engine) =>
        {
            var tools = engine.GetToolDefinitions();
            return Results.Ok(new { tools });
        });

        // 3. SSE 下行握手端点: GET /mcp/sse
        group.MapGet("/mcp/sse", async (HttpContext context) =>
        {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";

            var sessionId = Guid.NewGuid().ToString("N");
            var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleWriter = false,
                SingleReader = true
            });
            SseSessions[sessionId] = channel;

            // 握手指示端点
            var endpointUrl = $"{context.Request.PathBase}{context.Request.Path.Value?.Replace("/sse", "/message")}?sessionId={sessionId}";
            await context.Response.WriteAsync($"event: endpoint\r\ndata: {endpointUrl}\r\n\r\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);

            try
            {
                await foreach (var msg in channel.Reader.ReadAllAsync(context.RequestAborted))
                {
                    await context.Response.WriteAsync($"event: message\r\ndata: {msg}\r\n\r\n", context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                channel.Writer.TryComplete();
                SseSessions.TryRemove(sessionId, out _);
            }
        });

        // 4. SSE 上行消息接收端点: POST /mcp/message
        group.MapPost("/mcp/message", async (
            HttpContext context,
            IMcpServerEngine engine,
            IServiceProvider sp,
            CancellationToken ct) =>
        {
            var sessionId = context.Request.Query["sessionId"].ToString();
            if (string.IsNullOrWhiteSpace(sessionId) || !SseSessions.TryGetValue(sessionId, out var channel))
            {
                return Results.NotFound(new { error = "Session not found" });
            }

            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync(ct);
            if (string.IsNullOrWhiteSpace(body)) return Results.Accepted();

            var rpcReq = JsonSerializer.Deserialize<McpRpcRequest>(body);
            if (rpcReq == null) return Results.BadRequest();

            var rpcResp = await engine.HandleRequestAsync(rpcReq, sp, ct);
            var respJson = JsonSerializer.Serialize(rpcResp);

            channel.Writer.TryWrite(respJson);
            return Results.Accepted();
        });

        return group;
    }
}
