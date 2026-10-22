using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using TemporaryDiagnostics.Core.Common;
using TemporaryDiagnostics.Core.Contracts;
using TemporaryDiagnostics.Core.Models;

namespace TemporaryDiagnostics.AspNetCore.Endpoints;

public static class FileEndpoints
{
    public static RouteGroupBuilder MapFileEndpoints(this RouteGroupBuilder group)
    {
        var fileGroup = group.MapGroup("/api/files");

        fileGroup.MapGet("/list", async ([FromQuery] string? path, ISecureFileService fileService, CancellationToken ct) =>
        {
            var result = await fileService.ListDirectoryAsync(path, ct);
            return result.IsSuccess ? Results.Ok(result.Data) : Results.Json(result, statusCode: result.StatusCode);
        });

        fileGroup.MapGet("/read", async ([FromQuery] string path, ISecureFileService fileService, CancellationToken ct) =>
        {
            var result = await fileService.ReadFileTextAsync(path, ct);
            return result.IsSuccess ? Results.Ok(result.Data) : Results.Json(result, statusCode: result.StatusCode);
        });

        fileGroup.MapPost("/save", async ([FromBody] FileSaveRequest req, ISecureFileService fileService, CancellationToken ct) =>
        {
            var result = await fileService.SaveFileTextAsync(req.FullPath, req.Content, ct);
            return result.IsSuccess ? Results.Ok(result) : Results.Json(result, statusCode: result.StatusCode);
        });

        fileGroup.MapGet("/download", async ([FromQuery] string path, [FromQuery] bool? archive, ISecureFileService fileService, CancellationToken ct) =>
        {
            var result = await fileService.DownloadFileAsync(path, archive ?? false, ct);
            if (!result.IsSuccess)
            {
                return Results.Json(result, statusCode: result.StatusCode);
            }

            var (stream, contentType, fileName) = result.Data!;
            return Results.File(stream, contentType, fileName);
        });

        fileGroup.MapPost("/mkdir", async ([FromBody] DirectoryCreateRequest req, ISecureFileService fileService, CancellationToken ct) =>
        {
            var result = await fileService.CreateDirectoryAsync(req.ParentPath, req.DirectoryName, ct);
            return result.IsSuccess ? Results.Ok(result) : Results.Json(result, statusCode: result.StatusCode);
        });

        fileGroup.MapDelete("/delete", async ([FromQuery] string path, ISecureFileService fileService, CancellationToken ct) =>
        {
            var result = await fileService.DeletePathAsync(path, ct);
            return result.IsSuccess ? Results.Ok(result) : Results.Json(result, statusCode: result.StatusCode);
        });

        fileGroup.MapPost("/upload", async (HttpRequest request, ISecureFileService fileService, CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
            {
                return Results.BadRequest(Result.Failure("请求必须为表单格式 (multipart/form-data)"));
            }

            var form = await request.ReadFormAsync(ct);
            var targetDir = form["targetDirectory"].ToString();
            var file = form.Files.FirstOrDefault();

            if (file == null || string.IsNullOrWhiteSpace(targetDir))
            {
                return Results.BadRequest(Result.Failure("缺失目标路径或待上传文件"));
            }

            await using var stream = file.OpenReadStream();
            var result = await fileService.UploadFileAsync(targetDir, file.FileName, stream, ct);
            return result.IsSuccess ? Results.Ok(result) : Results.Json(result, statusCode: result.StatusCode);
        });

        return group;
    }
}
