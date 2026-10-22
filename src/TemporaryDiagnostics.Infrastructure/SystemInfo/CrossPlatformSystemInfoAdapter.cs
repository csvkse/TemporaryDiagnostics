using System.Collections;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using TemporaryDiagnostics.Core.Contracts;
using TemporaryDiagnostics.Core.Models;

namespace TemporaryDiagnostics.Infrastructure.SystemInfo;

/// <summary>
/// 跨平台操作系统与硬件指标适配器（实现 ISystemInfoAdapter）。
/// </summary>
public sealed class CrossPlatformSystemInfoAdapter : ISystemInfoAdapter
{
    private readonly IConfiguration? _configuration;

    public CrossPlatformSystemInfoAdapter(IConfiguration? configuration = null)
    {
        _configuration = configuration;
    }

    public Task<SystemOverview> GetSystemOverviewAsync(CancellationToken cancellationToken = default)
    {
        var (totalMem, availMem) = GetMemoryMetrics();
        var drives = GetDrives();
        var nics = GetNetworkInterfaces();

        var overview = new SystemOverview
        {
            MachineName = Environment.MachineName,
            OsDescription = RuntimeInformation.OSDescription,
            OsArchitecture = RuntimeInformation.OSArchitecture.ToString(),
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            FrameworkDescription = RuntimeInformation.FrameworkDescription,
            ProcessorCount = Environment.ProcessorCount,
            SystemUptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
            TotalPhysicalMemoryBytes = totalMem,
            AvailablePhysicalMemoryBytes = availMem,
            Drives = drives,
            NetworkInterfaces = nics
        };

        return Task.FromResult(overview);
    }

    public Task<EnvironmentInfo> GetEnvironmentInfoAsync(CancellationToken cancellationToken = default)
    {
        var envVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                envVars[key] = MaskIfSensitive(key, value);
            }
        }

        var configEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_configuration != null)
        {
            foreach (var kv in _configuration.AsEnumerable())
            {
                if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null)
                {
                    configEntries[kv.Key] = MaskIfSensitive(kv.Key, kv.Value);
                }
            }
        }

        return Task.FromResult(new EnvironmentInfo
        {
            EnvironmentVariables = envVars,
            ConfigurationEntries = configEntries
        });
    }

    public IReadOnlyList<AssemblyItem> GetLoadedAssemblies()
    {
        var list = new List<AssemblyItem>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var name = asm.GetName();
                list.Add(new AssemblyItem
                {
                    Name = name.Name ?? asm.FullName ?? "Unknown",
                    Version = name.Version?.ToString() ?? "0.0.0.0",
                    Location = asm.IsDynamic ? "(Dynamic)" : (asm.Location ?? string.Empty),
                    IsDynamic = asm.IsDynamic
                });
            }
            catch
            {
                // 忽略个别特殊动态程序集反射异常
            }
        }

        return list.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static (long Total, long Available) GetMemoryMetrics()
    {
        if (OperatingSystem.IsWindows())
        {
            var memStatus = new MEMORYSTATUSEX();
            memStatus.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
            if (GlobalMemoryStatusEx(ref memStatus))
            {
                return ((long)memStatus.ullTotalPhys, (long)memStatus.ullAvailPhys);
            }
        }
        else if (OperatingSystem.IsLinux() && File.Exists("/proc/meminfo"))
        {
            try
            {
                long totalKb = 0;
                long availKb = 0;
                foreach (var line in File.ReadLines("/proc/meminfo"))
                {
                    if (line.StartsWith("MemTotal:", StringComparison.OrdinalIgnoreCase))
                    {
                        totalKb = ParseProcMemLine(line);
                    }
                    else if (line.StartsWith("MemAvailable:", StringComparison.OrdinalIgnoreCase))
                    {
                        availKb = ParseProcMemLine(line);
                    }
                    if (totalKb > 0 && availKb > 0) break;
                }
                if (totalKb > 0)
                {
                    return (totalKb * 1024, availKb * 1024);
                }
            }
            catch
            {
                // 回退到 GC 内存
            }
        }

        // 回退默认：由 GC 分配估计
        var gcInfo = GC.GetGCMemoryInfo();
        return (gcInfo.TotalAvailableMemoryBytes, gcInfo.TotalAvailableMemoryBytes - GC.GetTotalMemory(false));
    }

    private static long ParseProcMemLine(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && long.TryParse(parts[1], out var val))
        {
            return val;
        }
        return 0;
    }

    private static List<DriveItem> GetDrives()
    {
        var items = new List<DriveItem>();
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    var isReady = d.IsReady;
                    items.Add(new DriveItem
                    {
                        Name = d.Name,
                        VolumeLabel = isReady ? d.VolumeLabel : string.Empty,
                        DriveType = d.DriveType.ToString(),
                        FileSystem = isReady ? d.DriveFormat : string.Empty,
                        TotalSizeBytes = isReady ? d.TotalSize : 0,
                        FreeSizeBytes = isReady ? d.AvailableFreeSpace : 0,
                        IsReady = isReady
                    });
                }
                catch
                {
                    // 忽略驱动器异常
                }
            }
        }
        catch
        {
            // 忽略列举驱动器异常
        }
        return items;
    }

    private static List<NetworkInterfaceItem> GetNetworkInterfaces()
    {
        var items = new List<NetworkInterfaceItem>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                var ipAddresses = new List<string>();
                try
                {
                    var ipProps = nic.GetIPProperties();
                    foreach (var addr in ipProps.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork ||
                            addr.Address.AddressFamily == AddressFamily.InterNetworkV6)
                        {
                            ipAddresses.Add(addr.Address.ToString());
                        }
                    }
                }
                catch { }

                var stats = nic.GetIPv4Statistics();

                items.Add(new NetworkInterfaceItem
                {
                    Name = nic.Name,
                    Description = nic.Description,
                    InterfaceType = nic.NetworkInterfaceType.ToString(),
                    OperationalStatus = nic.OperationalStatus.ToString(),
                    MacAddress = nic.GetPhysicalAddress().ToString(),
                    IpAddresses = ipAddresses,
                    BytesReceived = stats.BytesReceived,
                    BytesSent = stats.BytesSent
                });
            }
        }
        catch { }
        return items;
    }

    private static string MaskIfSensitive(string key, string value)
    {
        var upperKey = key.ToUpperInvariant();
        if (upperKey.Contains("KEY") ||
            upperKey.Contains("SECRET") ||
            upperKey.Contains("PASSWORD") ||
            upperKey.Contains("PWD") ||
            upperKey.Contains("TOKEN") ||
            upperKey.Contains("CONNECTIONSTRING") ||
            upperKey.Contains("CREDENTIAL") ||
            upperKey.Contains("AUTH"))
        {
            return "******";
        }
        return value;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] ref MEMORYSTATUSEX lpBuffer);
}
