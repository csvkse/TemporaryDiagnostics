using System.Diagnostics;
using TemporaryDiagnostics.Core.Contracts;
using TemporaryDiagnostics.Core.Models;

namespace TemporaryDiagnostics.Infrastructure.SystemInfo;

/// <summary>
/// .NET 运行时健康体征与进程资源采集器（实现 IRuntimeVitalsCollector）。
/// </summary>
public sealed class RuntimeVitalsCollector : IRuntimeVitalsCollector
{
    private DateTime _lastCpuSampleTime = DateTime.UtcNow;
    private TimeSpan _lastTotalProcessorTime = TimeSpan.Zero;
    private double _lastCalculatedCpuPercent = 0.0;
    private readonly object _cpuLock = new();

    public RuntimeVitalsCollector()
    {
        try
        {
            using var proc = Process.GetCurrentProcess();
            _lastTotalProcessorTime = proc.TotalProcessorTime;
            _lastCpuSampleTime = DateTime.UtcNow;
        }
        catch { }
    }

    public RuntimeVitals GetCurrentVitals()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();

        var cpu = CalculateCpuUsage(process);

        ThreadPool.GetAvailableThreads(out var availWorker, out var availCompletion);
        ThreadPool.GetMaxThreads(out var maxWorker, out var maxCompletion);

        var gcInfo = GC.GetGCMemoryInfo();

        return new RuntimeVitals
        {
            Timestamp = DateTime.UtcNow,
            ProcessId = process.Id,
            ProcessUptime = DateTime.UtcNow - process.StartTime.ToUniversalTime(),
            CpuPercent = Math.Round(cpu, 1),
            WorkingSetBytes = process.WorkingSet64,
            PrivateMemoryBytes = process.PrivateMemorySize64,
            ThreadCount = process.Threads.Count,
            HandleCount = process.HandleCount,

            Gen0Collections = GC.CollectionCount(0),
            Gen1Collections = GC.CollectionCount(1),
            Gen2Collections = GC.CollectionCount(2),
            TotalAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false),
            HeapSizeBytes = gcInfo.HeapSizeBytes,
            GcPauseTimePercent = Math.Round(gcInfo.PauseTimePercentage, 2),

            ThreadPoolAvailableWorkers = availWorker,
            ThreadPoolAvailableCompletionPorts = availCompletion,
            ThreadPoolMaxWorkers = maxWorker,
            ThreadPoolMaxCompletionPorts = maxCompletion
        };
    }

    private double CalculateCpuUsage(Process process)
    {
        lock (_cpuLock)
        {
            var now = DateTime.UtcNow;
            var timeDiff = (now - _lastCpuSampleTime).TotalMilliseconds;

            if (timeDiff >= 500)
            {
                try
                {
                    var currentProcessorTime = process.TotalProcessorTime;
                    var cpuUsedMs = (currentProcessorTime - _lastTotalProcessorTime).TotalMilliseconds;
                    var totalAvailableMs = timeDiff * Environment.ProcessorCount;

                    if (totalAvailableMs > 0)
                    {
                        _lastCalculatedCpuPercent = Math.Clamp((cpuUsedMs / totalAvailableMs) * 100.0, 0.0, 100.0);
                    }

                    _lastTotalProcessorTime = currentProcessorTime;
                    _lastCpuSampleTime = now;
                }
                catch
                {
                    // 忽略进程句柄访问异常
                }
            }

            return _lastCalculatedCpuPercent;
        }
    }
}
