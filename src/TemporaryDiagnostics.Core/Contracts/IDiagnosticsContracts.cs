using TemporaryDiagnostics.Core.Common;
using TemporaryDiagnostics.Core.Models;

namespace TemporaryDiagnostics.Core.Contracts;

/// <summary>
/// 依赖注入与动态方法调用引擎契约（支持反射与 IL Emit 策略）。
/// </summary>
public interface IDynamicInvoker
{
    IReadOnlyList<DiServiceItem> GetRegisteredServices(IServiceProvider serviceProvider);
    Result<IReadOnlyList<MethodDescriptor>> GetServiceMethods(string serviceTypeName);
    Task<DynamicInvokeResponse> InvokeAsync(DynamicInvokeRequest request, IServiceProvider serviceProvider, CancellationToken cancellationToken = default);
}

/// <summary>
/// 安全文件管理与安全沙箱契约。
/// </summary>
public interface ISecureFileService
{
    Task<Result<FileListResult>> ListDirectoryAsync(string? path, CancellationToken cancellationToken = default);
    Task<Result<FileReadResult>> ReadFileTextAsync(string path, CancellationToken cancellationToken = default);
    Task<Result> SaveFileTextAsync(string path, string content, CancellationToken cancellationToken = default);
    Task<Result<(Stream Stream, string ContentType, string FileName)>> DownloadFileAsync(string path, bool asArchive = false, CancellationToken cancellationToken = default);
    Task<Result> CreateDirectoryAsync(string parentPath, string directoryName, CancellationToken cancellationToken = default);
    Task<Result> DeletePathAsync(string path, CancellationToken cancellationToken = default);
    Task<Result> UploadFileAsync(string targetDirectory, string fileName, Stream fileStream, CancellationToken cancellationToken = default);
}

/// <summary>
/// 诊断日志与 HTTP 遥测数据存储槽契约。
/// </summary>
public interface IDiagnosticLogSink
{
    void RecordLog(DiagnosticLogItem item);
    void RecordHttpRequest(DiagnosticHttpRequestItem item);
    void RecordException(DiagnosticExceptionItem item);

    IReadOnlyList<DiagnosticLogItem> GetRecentLogs(int limit = 100);
    IReadOnlyList<DiagnosticHttpRequestItem> GetRecentHttpRequests(int limit = 100);
    IReadOnlyList<DiagnosticExceptionItem> GetRecentExceptions(int limit = 100);
    void Clear();
}

/// <summary>
/// MCP (Model Context Protocol) 核心协议引擎契约。
/// </summary>
public interface IMcpServerEngine
{
    Task<McpRpcResponse> HandleRequestAsync(McpRpcRequest request, IServiceProvider serviceProvider, CancellationToken cancellationToken = default);
    IReadOnlyList<McpToolDefinition> GetToolDefinitions();
}
