namespace TemporaryDiagnostics.Core.Models;

/// <summary>
/// 依赖注入容器中注册的服务项。
/// </summary>
public sealed class DiServiceItem
{
    public string ServiceType { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Lifetime { get; init; } = "Singleton";
    public string ImplementationType { get; init; } = string.Empty;
}

/// <summary>
/// 方法参数描述。
/// </summary>
public sealed class ParameterDescriptor
{
    public string Name { get; init; } = string.Empty;
    public string TypeName { get; init; } = string.Empty;
    public bool HasDefaultValue { get; init; }
    public object? DefaultValue { get; init; }
}

/// <summary>
/// 方法签名描述。
/// </summary>
public sealed class MethodDescriptor
{
    public string Name { get; init; } = string.Empty;
    public string ReturnTypeName { get; init; } = string.Empty;
    public bool IsAsync { get; init; }
    public bool IsStatic { get; init; }
    public List<ParameterDescriptor> Parameters { get; init; } = new();
}

/// <summary>
/// 动态方法调用请求。
/// </summary>
public sealed class DynamicInvokeRequest
{
    public string ServiceTypeName { get; set; } = string.Empty;
    public string MethodName { get; set; } = string.Empty;
    public List<object?>? Parameters { get; set; }
    public bool UseFastEmit { get; set; } = true;
}

/// <summary>
/// 动态方法调用结果。
/// </summary>
public sealed class DynamicInvokeResponse
{
    public bool Success { get; init; }
    public object? Result { get; init; }
    public string? ReturnTypeName { get; init; }
    public long ElapsedMicroseconds { get; init; }
    public string StrategyUsed { get; init; } = "EmitFastInvoker";
    public string? Error { get; init; }
}
