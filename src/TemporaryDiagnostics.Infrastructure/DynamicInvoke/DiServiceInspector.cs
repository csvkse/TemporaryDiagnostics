using System.Collections;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using TemporaryDiagnostics.Core.Models;

namespace TemporaryDiagnostics.Infrastructure.DynamicInvoke;

/// <summary>
/// 依赖注入服务探查器，能够遍历 IServiceCollection 与已注册类型。
/// </summary>
public static class DiServiceInspector
{
    private static IServiceCollection? _capturedServiceCollection;

    public static void SetServiceCollection(IServiceCollection services)
    {
        _capturedServiceCollection = services;
    }

    public static IReadOnlyList<DiServiceItem> InspectServices(IServiceProvider serviceProvider)
    {
        var items = new List<DiServiceItem>();

        if (_capturedServiceCollection != null)
        {
            foreach (var descriptor in _capturedServiceCollection)
            {
                var serviceType = descriptor.ServiceType;
                var implType = descriptor.ImplementationType?.FullName
                    ?? descriptor.ImplementationInstance?.GetType().FullName
                    ?? descriptor.ImplementationFactory?.Method.ReturnType.FullName
                    ?? "(Factory/Instance)";

                items.Add(new DiServiceItem
                {
                    ServiceType = serviceType.Name,
                    FullName = serviceType.FullName ?? serviceType.Name,
                    Lifetime = descriptor.Lifetime.ToString(),
                    ImplementationType = implType
                });
            }
        }
        else
        {
            // 回退方案：通过反射探查内部 IServiceProviderEngine 或已加载程序集
            try
            {
                var engineField = serviceProvider.GetType().GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? serviceProvider.GetType().GetProperty("Engine", BindingFlags.Instance | BindingFlags.NonPublic)?.GetMethod?.Invoke(serviceProvider, null) as FieldInfo;

                if (engineField != null)
                {
                    var engine = engineField.GetValue(serviceProvider);
                    if (engine != null)
                    {
                        var descriptorsProp = engine.GetType().GetProperty("Descriptors", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (descriptorsProp?.GetValue(engine) is IEnumerable descriptors)
                        {
                            foreach (var desc in descriptors)
                            {
                                if (desc is ServiceDescriptor d)
                                {
                                    items.Add(new DiServiceItem
                                    {
                                        ServiceType = d.ServiceType.Name,
                                        FullName = d.ServiceType.FullName ?? d.ServiceType.Name,
                                        Lifetime = d.Lifetime.ToString(),
                                        ImplementationType = d.ImplementationType?.FullName ?? "(Dynamic)"
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // 忽略非关键探查异常
            }
        }

        // 去重与排序
        return items
            .GroupBy(x => x.FullName)
            .Select(g => g.First())
            .OrderBy(x => x.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<MethodDescriptor> GetPublicMethods(Type type)
    {
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => !m.IsSpecialName && m.DeclaringType != typeof(object))
            .ToList();

        var descriptors = new List<MethodDescriptor>();
        foreach (var m in methods)
        {
            var isAsync = typeof(Task).IsAssignableFrom(m.ReturnType) ||
                          m.ReturnType.Name.StartsWith("ValueTask", StringComparison.Ordinal);

            var parameters = m.GetParameters().Select(p => new ParameterDescriptor
            {
                Name = p.Name ?? "arg",
                TypeName = p.ParameterType.Name,
                HasDefaultValue = p.HasDefaultValue,
                DefaultValue = p.HasDefaultValue ? p.DefaultValue : null
            }).ToList();

            descriptors.Add(new MethodDescriptor
            {
                Name = m.Name,
                ReturnTypeName = m.ReturnType.Name,
                IsAsync = isAsync,
                IsStatic = m.IsStatic,
                Parameters = parameters
            });
        }

        return descriptors;
    }
}
