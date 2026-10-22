namespace TemporaryDiagnostics.Core.Common;

/// <summary>
/// 表示不包含返回数据的操作结果（借鉴 Reader.Core 函数式规范）。
/// </summary>
public class Result
{
    public bool IsSuccess { get; }
    public string? Error { get; }
    public int StatusCode { get; }

    protected Result(bool isSuccess, string? error, int statusCode)
    {
        IsSuccess = isSuccess;
        Error = error;
        StatusCode = statusCode;
    }

    public static Result Success(int statusCode = 200) => new(true, null, statusCode);

    public static Result Failure(string error, int statusCode = 400) =>
        new(false, error, statusCode);

    public static Result NotFound(string error = "请求的资源未找到") =>
        new(false, error, 404);

    public static Result Unauthorized(string error = "未授权访问或访问密钥无效") =>
        new(false, error, 401);

    public static Result Forbidden(string error = "没有权限执行该操作") =>
        new(false, error, 403);
}

/// <summary>
/// 泛型操作结果封装，用于服务层及领域层返回值。
/// </summary>
/// <typeparam name="T">成功时携带的数据类型</typeparam>
public class Result<T> : Result
{
    public T? Data { get; }

    private Result(bool isSuccess, T? data, string? error, int statusCode)
        : base(isSuccess, error, statusCode)
    {
        Data = data;
    }

    public static Result<T> Success(T data, int statusCode = 200) =>
        new(true, data, null, statusCode);

    public static new Result<T> Failure(string error, int statusCode = 400) =>
        new(false, default, error, statusCode);

    public static new Result<T> NotFound(string error = "请求的资源未找到") =>
        new(false, default, error, 404);

    public static new Result<T> Unauthorized(string error = "未授权访问或访问密钥无效") =>
        new(false, default, error, 401);

    public static new Result<T> Forbidden(string error = "没有权限执行该操作") =>
        new(false, default, error, 403);
}
