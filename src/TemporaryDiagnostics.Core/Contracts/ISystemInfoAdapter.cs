using TemporaryDiagnostics.Core.Models;

namespace TemporaryDiagnostics.Core.Contracts;

/// <summary>
/// 操作系统平台与硬件指标适配器（适配器模式）。
/// </summary>
public interface ISystemInfoAdapter
{
    Task<SystemOverview> GetSystemOverviewAsync(CancellationToken cancellationToken = default);
    Task<EnvironmentInfo> GetEnvironmentInfoAsync(CancellationToken cancellationToken = default);
    IReadOnlyList<AssemblyItem> GetLoadedAssemblies();
}

/// <summary>
/// .NET 运行时健康体征与进程资源采集器。
/// </summary>
public interface IRuntimeVitalsCollector
{
    RuntimeVitals GetCurrentVitals();
}
