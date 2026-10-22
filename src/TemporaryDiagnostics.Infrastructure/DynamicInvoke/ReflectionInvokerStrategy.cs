using System.Reflection;

namespace TemporaryDiagnostics.Infrastructure.DynamicInvoke;

/// <summary>
/// 传统反射方法调用策略，作为通用回退支持。
/// </summary>
public static class ReflectionInvokerStrategy
{
    public static async Task<object?> InvokeAsync(MethodInfo method, object? instance, object?[]? parameters)
    {
        var result = method.Invoke(instance, parameters);
        if (result == null) return null;

        if (result is Task task)
        {
            await task;
            var taskType = task.GetType();
            if (taskType.IsGenericType)
            {
                var resultProp = taskType.GetProperty("Result");
                return resultProp?.GetValue(task);
            }
            return null;
        }

        if (result.GetType().Name.StartsWith("ValueTask", StringComparison.Ordinal))
        {
            var asTaskMethod = result.GetType().GetMethod("AsTask");
            if (asTaskMethod != null)
            {
                var t = (Task)asTaskMethod.Invoke(result, null)!;
                await t;
                var prop = t.GetType().GetProperty("Result");
                return prop?.GetValue(t);
            }
        }

        return result;
    }
}
