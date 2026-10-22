using Microsoft.Extensions.Options;
using TemporaryDiagnostics.Core.Common;
using TemporaryDiagnostics.Core.Configuration;

namespace TemporaryDiagnostics.Infrastructure.FileSystem;

/// <summary>
/// 路径穿越与非法文件访问守卫。
/// </summary>
public sealed class PathTraversalGuard
{
    private readonly TemporaryDiagnosticsOptions _options;

    public PathTraversalGuard(IOptions<TemporaryDiagnosticsOptions> options)
    {
        _options = options.Value;
    }

    public Result<string> NormalizeAndValidate(string? requestedPath)
    {
        if (string.IsNullOrWhiteSpace(requestedPath) || requestedPath == "/")
        {
            return Result<string>.Success("/");
        }

        try
        {
            // 防御 Windows UNC 路径 (\\remote\share) 与特殊设备流 (CON, PRN, AUX, NUL, ::$DATA)
            if (requestedPath.StartsWith(@"\\") || requestedPath.Contains("::$DATA", StringComparison.OrdinalIgnoreCase))
            {
                return Result<string>.Forbidden("禁止访问 UNC 远程路径或 NTFS 数据流");
            }

            var fullPath = Path.GetFullPath(requestedPath);

            // 若开启全盘诊断模式，允许访问所有存在驱动器
            if (_options.AllowAllDrivesInDiagnostics)
            {
                return Result<string>.Success(fullPath);
            }

            // 严格白名单校验
            var isAllowed = _options.AllowedFileRoots.Any(root =>
            {
                var fullRoot = Path.GetFullPath(root);
                return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
                       fullPath.StartsWith(fullRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            });

            if (!isAllowed)
            {
                return Result<string>.Forbidden($"路径 [{fullPath}] 不在允许访问的白名单根目录中。");
            }

            return Result<string>.Success(fullPath);
        }
        catch (Exception ex)
        {
            return Result<string>.Failure($"非法路径格式: {ex.Message}");
        }
    }
}
