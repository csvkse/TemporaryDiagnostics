using System.Text.Json;
using System.Text.Json.Serialization;

namespace TemporaryDiagnostics.Core.Models;

/// <summary>
/// 标准 MCP JSON-RPC 2.0 请求。
/// </summary>
public sealed class McpRpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public object? Id { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }
}

/// <summary>
/// 标准 MCP JSON-RPC 2.0 响应。
/// </summary>
public sealed class McpRpcResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public object? Id { get; set; }

    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Result { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public McpRpcError? Error { get; set; }

    public static McpRpcResponse Success(object? id, object? result) => new()
    {
        Id = id,
        Result = result
    };

    public static McpRpcResponse Fail(object? id, int code, string message, object? data = null) => new()
    {
        Id = id,
        Error = new McpRpcError
        {
            Code = code,
            Message = message,
            Data = data
        }
    };
}

public sealed class McpRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Data { get; set; }
}

/// <summary>
/// MCP 工具定义元数据。
/// </summary>
public sealed class McpToolDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("inputSchema")]
    public JsonElement InputSchema { get; set; }
}

/// <summary>
/// MCP 工具执行结果。
/// </summary>
public sealed class McpCallToolResult
{
    [JsonPropertyName("content")]
    public List<McpContentBlock> Content { get; set; } = new();

    [JsonPropertyName("isError")]
    public bool IsError { get; set; }

    public static McpCallToolResult Text(string text, bool isError = false) => new()
    {
        IsError = isError,
        Content = new List<McpContentBlock>
        {
            new() { Type = "text", Text = text }
        }
    };
}

public sealed class McpContentBlock
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}
