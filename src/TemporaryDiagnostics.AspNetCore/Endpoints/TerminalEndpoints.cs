using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using TemporaryDiagnostics.Core.Common;
using TemporaryDiagnostics.Core.Contracts;

namespace TemporaryDiagnostics.AspNetCore.Endpoints;

public static class TerminalEndpoints
{
    public static RouteGroupBuilder MapTerminalEndpoints(this RouteGroupBuilder group)
    {
        var termGroup = group.MapGroup("/api/terminal");

        termGroup.MapGet("/sessions", (IPtyEngine engine) =>
        {
            var sessions = engine.ListActiveSessions().Select(s => new
            {
                s.Id,
                s.ProcessId,
                State = s.State.ToString(),
                s.IsActive
            });
            return Results.Ok(sessions);
        });

        termGroup.MapDelete("/sessions/{id}", async (string id, IPtyEngine engine, CancellationToken ct) =>
        {
            var closed = await engine.CloseSessionAsync(id, ct);
            return closed ? Results.Ok(Result.Success()) : Results.NotFound(Result.NotFound("未找到该终端会话"));
        });

        // WebSocket 终端双向流
        termGroup.MapGet("/ws", async (HttpContext context, IPtyEngine ptyEngine) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("WebSocket request expected.");
                return;
            }

            int cols = 120;
            int rows = 32;
            if (context.Request.Query.TryGetValue("cols", out var cVal) && int.TryParse(cVal, out var c)) cols = c;
            if (context.Request.Query.TryGetValue("rows", out var rVal) && int.TryParse(rVal, out var r)) rows = r;

            using var webSocket = await context.WebSockets.AcceptWebSocketAsync();

            IPtySession? session = null;
            try
            {
                session = await ptyEngine.StartSessionAsync(new PtyStartOptions
                {
                    Columns = cols,
                    Rows = rows
                }, context.RequestAborted);
            }
            catch (Exception ex)
            {
                var errBytes = Encoding.UTF8.GetBytes($"\r\n\x1b[31m[PTY Error] 启动终端会话失败: {ex.Message}\x1b[0m\r\n");
                await webSocket.SendAsync(errBytes, WebSocketMessageType.Text, true, CancellationToken.None);
                await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, "PTY Spawn Failed", CancellationToken.None);
                return;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);

            // 1. PTY -> WebSocket 输出推送任务
            var ptyToWsTask = Task.Run(async () =>
            {
                var buffer = new byte[4096];
                try
                {
                    while (!cts.IsCancellationRequested && session.IsActive && webSocket.State == WebSocketState.Open)
                    {
                        var read = await session.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                        if (read <= 0) break;

                        await webSocket.SendAsync(
                            new ArraySegment<byte>(buffer, 0, read),
                            WebSocketMessageType.Binary,
                            true,
                            cts.Token);
                    }
                }
                catch (OperationCanceledException) { }
                catch { }
            }, cts.Token);

            // 2. WebSocket -> PTY 输入写入任务
            var wsToPtyTask = Task.Run(async () =>
            {
                var buffer = new byte[4096];
                try
                {
                    while (!cts.IsCancellationRequested && session.IsActive && webSocket.State == WebSocketState.Open)
                    {
                        var receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                        if (receiveResult.MessageType == WebSocketMessageType.Close)
                        {
                            break;
                        }

                        if (receiveResult.Count > 0)
                        {
                            // 检查是否为 JSON 终端控制指令 (如 resize: {"type":"resize","cols":120,"rows":30})
                            if (buffer[0] == (byte)'{' && receiveResult.Count < 256)
                            {
                                try
                                {
                                    var text = Encoding.UTF8.GetString(buffer, 0, receiveResult.Count);
                                    using var doc = JsonDocument.Parse(text);
                                    if (doc.RootElement.TryGetProperty("type", out var typeProp) &&
                                        typeProp.GetString() == "resize")
                                    {
                                        var newCols = doc.RootElement.GetProperty("cols").GetInt32();
                                        var newRows = doc.RootElement.GetProperty("rows").GetInt32();
                                        await session.ResizeAsync(newCols, newRows, cts.Token);
                                        continue;
                                    }
                                }
                                catch
                                {
                                    // 非控制指令，按普通字符转发
                                }
                            }

                            await session.WriteAsync(buffer, 0, receiveResult.Count, cts.Token);
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch { }
            }, cts.Token);

            try
            {
                await Task.WhenAny(ptyToWsTask, wsToPtyTask);
            }
            finally
            {
                cts.Cancel();

                if (webSocket.State == WebSocketState.Open)
                {
                    try
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Session ended", CancellationToken.None);
                    }
                    catch { }
                }

                if (session != null)
                {
                    await ptyEngine.CloseSessionAsync(session.Id, CancellationToken.None);
                }
            }
        });

        return group;
    }
}
