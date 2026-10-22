using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TemporaryDiagnostics.Core.Common;
using TemporaryDiagnostics.Core.Contracts;
using TemporaryDiagnostics.Core.Models;

namespace TemporaryDiagnostics.Infrastructure.DynamicInvoke;

/// <summary>
/// 动态方法调用协调器，整合服务探查、类型与方法缓存、参数强制类型反序列化与 Fast Emit / 反射调度。
/// </summary>
public sealed class DynamicInvokeCoordinator : IDynamicInvoker
{
    private static readonly ConcurrentDictionary<string, Type?> TypeCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<(Type, string, int), MethodInfo?> MethodCache = new();

    public IReadOnlyList<DiServiceItem> GetRegisteredServices(IServiceProvider serviceProvider)
    {
        return DiServiceInspector.InspectServices(serviceProvider);
    }

    public Result<IReadOnlyList<MethodDescriptor>> GetServiceMethods(string serviceTypeName)
    {
        var targetType = ResolveTypeCached(serviceTypeName);
        if (targetType == null)
        {
            return Result<IReadOnlyList<MethodDescriptor>>.NotFound($"无法定位类型: {serviceTypeName}");
        }

        var descriptors = DiServiceInspector.GetPublicMethods(targetType);
        return Result<IReadOnlyList<MethodDescriptor>>.Success(descriptors);
    }

    public async Task<DynamicInvokeResponse> InvokeAsync(
        DynamicInvokeRequest request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        var strategy = request.UseFastEmit ? "EmitFastInvoker" : "ReflectionInvoker";

        try
        {
            var targetType = ResolveTypeCached(request.ServiceTypeName);
            if (targetType == null)
            {
                return new DynamicInvokeResponse
                {
                    Success = false,
                    Error = $"无法解析类型: {request.ServiceTypeName}",
                    StrategyUsed = strategy
                };
            }

            // 获取目标实例：静态方法为 null，否则优先从 DI 容器获取
            object? instance = null;
            using var scope = serviceProvider.CreateScope();
            instance = scope.ServiceProvider.GetService(targetType)
                       ?? ActivatorUtilities.CreateInstance(scope.ServiceProvider, targetType);

            var inputParams = request.Parameters ?? new List<object?>();
            var targetMethod = ResolveMethodCached(targetType, request.MethodName, inputParams.Count);

            if (targetMethod == null)
            {
                return new DynamicInvokeResponse
                {
                    Success = false,
                    Error = $"类型 {targetType.Name} 中未找到公共方法 {request.MethodName}",
                    StrategyUsed = strategy
                };
            }

            var methodParams = targetMethod.GetParameters();

            // 转换入参
            var convertedArgs = new object?[methodParams.Length];
            for (var i = 0; i < methodParams.Length; i++)
            {
                if (i < inputParams.Count)
                {
                    convertedArgs[i] = ConvertArgument(inputParams[i], methodParams[i].ParameterType);
                }
                else if (methodParams[i].HasDefaultValue)
                {
                    convertedArgs[i] = methodParams[i].DefaultValue;
                }
                else
                {
                    convertedArgs[i] = methodParams[i].ParameterType.IsValueType
                        ? Activator.CreateInstance(methodParams[i].ParameterType)
                        : null;
                }
            }

            // 执行调用
            object? result;
            if (request.UseFastEmit)
            {
                result = await EmitFastInvokerStrategy.InvokeAsync(targetMethod, instance, convertedArgs);
            }
            else
            {
                result = await ReflectionInvokerStrategy.InvokeAsync(targetMethod, instance, convertedArgs);
            }

            var elapsedMicroseconds = (long)((Stopwatch.GetTimestamp() - startTimestamp) * (1_000_000.0 / Stopwatch.Frequency));

            return new DynamicInvokeResponse
            {
                Success = true,
                Result = result,
                ReturnTypeName = targetMethod.ReturnType.Name,
                ElapsedMicroseconds = elapsedMicroseconds,
                StrategyUsed = strategy
            };
        }
        catch (Exception ex)
        {
            var elapsedMicroseconds = (long)((Stopwatch.GetTimestamp() - startTimestamp) * (1_000_000.0 / Stopwatch.Frequency));
            var innerEx = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
            return new DynamicInvokeResponse
            {
                Success = false,
                Error = $"{innerEx.GetType().Name}: {innerEx.Message}",
                ElapsedMicroseconds = elapsedMicroseconds,
                StrategyUsed = strategy
            };
        }
    }

    private static Type? ResolveTypeCached(string typeName)
    {
        return TypeCache.GetOrAdd(typeName, ResolveTypeDirect);
    }

    private static MethodInfo? ResolveMethodCached(Type targetType, string methodName, int paramCount)
    {
        return MethodCache.GetOrAdd((targetType, methodName, paramCount), key =>
        {
            var (type, name, count) = key;
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (methods.Count == 0) return null;

            return methods.FirstOrDefault(m => m.GetParameters().Length == count) ?? methods[0];
        });
    }

    private static Type? ResolveTypeDirect(string typeName)
    {
        // 1. 尝试直接 Type.GetType
        var type = Type.GetType(typeName, false, true);
        if (type != null) return type;

        // 2. 遍历当前已加载程序集
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = asm.GetType(typeName, false, true);
            if (type != null) return type;

            var bySimpleName = asm.GetTypes().FirstOrDefault(t =>
                t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase) ||
                (t.FullName != null && t.FullName.EndsWith("." + typeName, StringComparison.OrdinalIgnoreCase)));

            if (bySimpleName != null) return bySimpleName;
        }

        return null;
    }

    private static object? ConvertArgument(object? value, Type targetType)
    {
        if (value == null)
        {
            return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
        }

        if (targetType.IsInstanceOfType(value))
        {
            return value;
        }

        if (value is JsonElement element)
        {
            var rawText = element.GetRawText();
            if (targetType == typeof(string) && element.ValueKind == JsonValueKind.String)
            {
                return element.GetString();
            }
            return JsonSerializer.Deserialize(rawText, targetType);
        }

        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlyingType == typeof(Guid))
        {
            return Guid.Parse(value.ToString()!);
        }

        if (underlyingType.IsEnum)
        {
            return Enum.Parse(underlyingType, value.ToString()!, true);
        }

        return Convert.ChangeType(value, underlyingType);
    }
}
