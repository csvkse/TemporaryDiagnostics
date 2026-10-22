using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Options;
using TemporaryDiagnostics.Core.Common;
using TemporaryDiagnostics.Core.Configuration;
using TemporaryDiagnostics.Core.Contracts;
using TemporaryDiagnostics.Core.Models;

namespace TemporaryDiagnostics.Infrastructure.FileSystem;

/// <summary>
/// 安全文件管理服务实现（实现 ISecureFileService）。
/// </summary>
public sealed class SecureFileService : ISecureFileService
{
    private readonly PathTraversalGuard _guard;
    private readonly TemporaryDiagnosticsOptions _options;

    public SecureFileService(PathTraversalGuard guard, IOptions<TemporaryDiagnosticsOptions> options)
    {
        _guard = guard;
        _options = options.Value;
    }

    public Task<Result<FileListResult>> ListDirectoryAsync(string? path, CancellationToken cancellationToken = default)
    {
        var validation = _guard.NormalizeAndValidate(path);
        if (!validation.IsSuccess)
        {
            return Task.FromResult(Result<FileListResult>.Forbidden(validation.Error!));
        }

        var normalized = validation.Data!;

        // 根目录特殊处理：Windows 下展示可用磁盘，Linux 下展示 /
        if (normalized == "/" || string.IsNullOrWhiteSpace(normalized))
        {
            if (OperatingSystem.IsWindows())
            {
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .Select(d => d.Name.Replace('\\', '/'))
                    .ToList();

                var driveItems = drives.Select(d => new FileItem
                {
                    Name = d,
                    FullPath = d,
                    IsDirectory = true,
                    LastModified = DateTime.UtcNow
                }).ToList();

                return Task.FromResult(Result<FileListResult>.Success(new FileListResult
                {
                    CurrentPath = "/",
                    ParentPath = null,
                    IsRoot = true,
                    Drives = drives,
                    Items = driveItems
                }));
            }
            normalized = "/";
        }

        if (!Directory.Exists(normalized))
        {
            return Task.FromResult(Result<FileListResult>.NotFound($"目标目录不存在: {normalized}"));
        }

        try
        {
            var dirInfo = new DirectoryInfo(normalized);
            var parentPath = dirInfo.Parent?.FullName.Replace('\\', '/');
            var items = new List<FileItem>();

            // 枚举子目录
            foreach (var subDir in dirInfo.GetDirectories())
            {
                try
                {
                    items.Add(new FileItem
                    {
                        Name = subDir.Name,
                        FullPath = subDir.FullName.Replace('\\', '/'),
                        IsDirectory = true,
                        SizeBytes = 0,
                        LastModified = subDir.LastWriteTimeUtc
                    });
                }
                catch { }
            }

            // 枚举文件
            foreach (var file in dirInfo.GetFiles())
            {
                try
                {
                    items.Add(new FileItem
                    {
                        Name = file.Name,
                        FullPath = file.FullName.Replace('\\', '/'),
                        IsDirectory = false,
                        SizeBytes = file.Length,
                        LastModified = file.LastWriteTimeUtc,
                        Extension = file.Extension
                    });
                }
                catch { }
            }

            var sortedItems = items
                .OrderByDescending(i => i.IsDirectory)
                .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var drivesList = OperatingSystem.IsWindows()
                ? DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.Name.Replace('\\', '/')).ToList()
                : new List<string> { "/" };

            return Task.FromResult(Result<FileListResult>.Success(new FileListResult
            {
                CurrentPath = normalized.Replace('\\', '/'),
                ParentPath = parentPath,
                IsRoot = parentPath == null,
                Drives = drivesList,
                Items = sortedItems
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<FileListResult>.Failure($"读取目录失败: {ex.Message}"));
        }
    }

    public async Task<Result<FileReadResult>> ReadFileTextAsync(string path, CancellationToken cancellationToken = default)
    {
        var validation = _guard.NormalizeAndValidate(path);
        if (!validation.IsSuccess)
        {
            return Result<FileReadResult>.Forbidden(validation.Error!);
        }

        var fullPath = validation.Data!;
        if (!File.Exists(fullPath))
        {
            return Result<FileReadResult>.NotFound($"文件未找到: {fullPath}");
        }

        try
        {
            var fileInfo = new FileInfo(fullPath);
            var size = fileInfo.Length;
            var isTruncated = size > _options.MaxTextFileSize;

            if (isTruncated)
            {
                await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var buffer = new char[4096];
                var sb = new StringBuilder();
                var remaining = _options.MaxTextFileSize;

                while (remaining > 0)
                {
                    var toRead = (int)Math.Min(buffer.Length, remaining);
                    var read = await reader.ReadBlockAsync(buffer, 0, toRead);
                    if (read <= 0) break;
                    sb.Append(buffer, 0, read);
                    remaining -= read;
                }

                sb.AppendLine();
                sb.AppendLine($"\n[诊断提示] 文件大小 ({size / 1024.0:F1} KB) 已超过在线文本预览上限 ({_options.MaxTextFileSize / 1024 / 1024} MB)，已截断显示。请使用下载功能获取完整文件。");

                return Result<FileReadResult>.Success(new FileReadResult
                {
                    FullPath = fullPath.Replace('\\', '/'),
                    Content = sb.ToString(),
                    SizeBytes = size,
                    IsTruncated = true
                });
            }

            var text = await File.ReadAllTextAsync(fullPath, Encoding.UTF8, cancellationToken);
            return Result<FileReadResult>.Success(new FileReadResult
            {
                FullPath = fullPath.Replace('\\', '/'),
                Content = text,
                SizeBytes = size,
                IsTruncated = false
            });
        }
        catch (Exception ex)
        {
            return Result<FileReadResult>.Failure($"读取文件失败: {ex.Message}");
        }
    }

    public async Task<Result> SaveFileTextAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        var validation = _guard.NormalizeAndValidate(path);
        if (!validation.IsSuccess)
        {
            return Result.Forbidden(validation.Error!);
        }

        var fullPath = validation.Data!;

        try
        {
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await File.WriteAllTextAsync(fullPath, content, Encoding.UTF8, cancellationToken);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"保存文件失败: {ex.Message}");
        }
    }

    public async Task<Result<(Stream Stream, string ContentType, string FileName)>> DownloadFileAsync(
        string path,
        bool asArchive = false,
        CancellationToken cancellationToken = default)
    {
        var validation = _guard.NormalizeAndValidate(path);
        if (!validation.IsSuccess)
        {
            return Result<(Stream, string, string)>.Forbidden(validation.Error!);
        }

        var fullPath = validation.Data!;

        // 1. 打包压缩下载目录
        if (asArchive || Directory.Exists(fullPath))
        {
            if (!Directory.Exists(fullPath))
            {
                return Result<(Stream, string, string)>.NotFound($"目录不存在: {fullPath}");
            }

            var memoryStream = new MemoryStream();
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var dirInfo = new DirectoryInfo(fullPath);
                var rootLen = dirInfo.FullName.Length + 1;

                foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var entryName = file.FullName.Length > rootLen
                            ? file.FullName.Substring(rootLen).Replace('\\', '/')
                            : file.Name;

                        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                        await using var entryStream = entry.Open();
                        await using var fileStream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        await fileStream.CopyToAsync(entryStream, cancellationToken);
                    }
                    catch
                    {
                        // 忽略锁定文件
                    }
                }
            }

            memoryStream.Seek(0, SeekOrigin.Begin);
            var zipName = $"{Path.GetFileName(fullPath.TrimEnd('/', '\\'))}_{DateTime.UtcNow:yyyyMMddHHmmss}.zip";
            return Result<(Stream, string, string)>.Success((memoryStream, "application/zip", zipName));
        }

        // 2. 单文件下载
        if (!File.Exists(fullPath))
        {
            return Result<(Stream, string, string)>.NotFound($"文件不存在: {fullPath}");
        }

        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var fileName = Path.GetFileName(fullPath);
        return Result<(Stream, string, string)>.Success((stream, "application/octet-stream", fileName));
    }

    public Task<Result> CreateDirectoryAsync(string parentPath, string directoryName, CancellationToken cancellationToken = default)
    {
        var targetCombined = Path.Combine(parentPath, directoryName);
        var validation = _guard.NormalizeAndValidate(targetCombined);
        if (!validation.IsSuccess)
        {
            return Task.FromResult(Result.Forbidden(validation.Error!));
        }

        try
        {
            Directory.CreateDirectory(validation.Data!);
            return Task.FromResult(Result.Success());
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result.Failure($"创建目录失败: {ex.Message}"));
        }
    }

    public Task<Result> DeletePathAsync(string path, CancellationToken cancellationToken = default)
    {
        var validation = _guard.NormalizeAndValidate(path);
        if (!validation.IsSuccess)
        {
            return Task.FromResult(Result.Forbidden(validation.Error!));
        }

        var fullPath = validation.Data!;

        // 根目录与驱动器严禁删除防护
        if (fullPath == "/" || (fullPath.Length <= 3 && fullPath.EndsWith(@":\")))
        {
            return Task.FromResult(Result.Forbidden("禁止删除系统根路径或驱动器分区！"));
        }

        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                return Task.FromResult(Result.Success());
            }

            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
                return Task.FromResult(Result.Success());
            }

            return Task.FromResult(Result.NotFound("未找到待删除的目标项"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result.Failure($"删除失败: {ex.Message}"));
        }
    }

    public async Task<Result> UploadFileAsync(string targetDirectory, string fileName, Stream fileStream, CancellationToken cancellationToken = default)
    {
        var combined = Path.Combine(targetDirectory, fileName);
        var validation = _guard.NormalizeAndValidate(combined);
        if (!validation.IsSuccess)
        {
            return Result.Forbidden(validation.Error!);
        }

        var fullPath = validation.Data!;
        try
        {
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await using var destStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await fileStream.CopyToAsync(destStream, cancellationToken);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"上传写入失败: {ex.Message}");
        }
    }
}
