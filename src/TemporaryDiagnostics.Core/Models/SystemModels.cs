namespace TemporaryDiagnostics.Core.Models;

/// <summary>
/// 操作系统与硬件综合概览信息。
/// </summary>
public sealed class SystemOverview
{
    public string MachineName { get; init; } = string.Empty;
    public string OsDescription { get; init; } = string.Empty;
    public string OsArchitecture { get; init; } = string.Empty;
    public string ProcessArchitecture { get; init; } = string.Empty;
    public string FrameworkDescription { get; init; } = string.Empty;
    public int ProcessorCount { get; init; }
    public TimeSpan SystemUptime { get; init; }
    public long TotalPhysicalMemoryBytes { get; init; }
    public long AvailablePhysicalMemoryBytes { get; init; }
    public double MemoryUsagePercent => TotalPhysicalMemoryBytes > 0
        ? Math.Round((1.0 - (double)AvailablePhysicalMemoryBytes / TotalPhysicalMemoryBytes) * 100.0, 1)
        : 0;

    public List<DriveItem> Drives { get; init; } = new();
    public List<NetworkInterfaceItem> NetworkInterfaces { get; init; } = new();
}

/// <summary>
/// 磁盘驱动器/挂载分区信息。
/// </summary>
public sealed class DriveItem
{
    public string Name { get; init; } = string.Empty;
    public string VolumeLabel { get; init; } = string.Empty;
    public string DriveType { get; init; } = string.Empty;
    public string FileSystem { get; init; } = string.Empty;
    public long TotalSizeBytes { get; init; }
    public long FreeSizeBytes { get; init; }
    public double UsagePercent => TotalSizeBytes > 0
        ? Math.Round((1.0 - (double)FreeSizeBytes / TotalSizeBytes) * 100.0, 1)
        : 0;
    public bool IsReady { get; init; }
}

/// <summary>
/// 网络适配器信息。
/// </summary>
public sealed class NetworkInterfaceItem
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string InterfaceType { get; init; } = string.Empty;
    public string OperationalStatus { get; init; } = string.Empty;
    public string MacAddress { get; init; } = string.Empty;
    public List<string> IpAddresses { get; init; } = new();
    public long BytesReceived { get; init; }
    public long BytesSent { get; init; }
}

/// <summary>
/// .NET 运行时健康体征与进程资源占用。
/// </summary>
public sealed class RuntimeVitals
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public int ProcessId { get; init; }
    public TimeSpan ProcessUptime { get; init; }
    public double CpuPercent { get; init; }
    public long WorkingSetBytes { get; init; }
    public long PrivateMemoryBytes { get; init; }
    public int ThreadCount { get; init; }
    public int HandleCount { get; init; }

    // GC 指标
    public int Gen0Collections { get; init; }
    public int Gen1Collections { get; init; }
    public int Gen2Collections { get; init; }
    public long TotalAllocatedBytes { get; init; }
    public long HeapSizeBytes { get; init; }
    public double GcPauseTimePercent { get; init; }

    // ThreadPool 指标
    public int ThreadPoolAvailableWorkers { get; init; }
    public int ThreadPoolAvailableCompletionPorts { get; init; }
    public int ThreadPoolMaxWorkers { get; init; }
    public int ThreadPoolMaxCompletionPorts { get; init; }
}

/// <summary>
/// 环境变量与配置集合（敏感键自动脱敏）。
/// </summary>
public sealed class EnvironmentInfo
{
    public Dictionary<string, string> EnvironmentVariables { get; init; } = new();
    public Dictionary<string, string> ConfigurationEntries { get; init; } = new();
}

/// <summary>
/// 已加载程序集描述。
/// </summary>
public sealed class AssemblyItem
{
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public bool IsDynamic { get; init; }
}
