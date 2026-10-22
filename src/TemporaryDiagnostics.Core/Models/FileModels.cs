namespace TemporaryDiagnostics.Core.Models;

/// <summary>
/// 文件或目录条目描述。
/// </summary>
public sealed class FileItem
{
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }
    public long SizeBytes { get; init; }
    public DateTime LastModified { get; init; }
    public string Extension { get; init; } = string.Empty;
}

/// <summary>
/// 目录列表查询结果。
/// </summary>
public sealed class FileListResult
{
    public string CurrentPath { get; init; } = string.Empty;
    public string? ParentPath { get; init; }
    public bool IsRoot { get; init; }
    public List<string> Drives { get; init; } = new();
    public List<FileItem> Items { get; init; } = new();
}

/// <summary>
/// 文本文件内容读取结果。
/// </summary>
public sealed class FileReadResult
{
    public string FullPath { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public bool IsTruncated { get; init; }
    public string EncodingName { get; init; } = "utf-8";
}

/// <summary>
/// 保存文本文件请求。
/// </summary>
public sealed class FileSaveRequest
{
    public string FullPath { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// 创建目录请求。
/// </summary>
public sealed class DirectoryCreateRequest
{
    public string ParentPath { get; set; } = string.Empty;
    public string DirectoryName { get; set; } = string.Empty;
}
