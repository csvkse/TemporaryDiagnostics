using Microsoft.Extensions.Configuration;
using TemporaryDiagnostics.Infrastructure.SystemInfo;
using Xunit;

namespace TemporaryDiagnostics.Tests;

public class SystemInfoAndVitalsTests
{
    [Fact]
    public async Task SystemInfoAdapter_ShouldCollectValidMetrics()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Password"] = "MySuperSecret123",
                ["App:Name"] = "DiagnosticsTestApp"
            })
            .Build();

        var adapter = new CrossPlatformSystemInfoAdapter(config);

        var overview = await adapter.GetSystemOverviewAsync();
        Assert.NotNull(overview);
        Assert.False(string.IsNullOrWhiteSpace(overview.MachineName));
        Assert.True(overview.ProcessorCount > 0);
        Assert.True(overview.TotalPhysicalMemoryBytes > 0);
        Assert.NotEmpty(overview.Drives);

        var env = await adapter.GetEnvironmentInfoAsync();
        Assert.NotNull(env);
        Assert.True(env.ConfigurationEntries.ContainsKey("Database:Password"));
        // 验证敏感信息自动脱敏
        Assert.Equal("******", env.ConfigurationEntries["Database:Password"]);
        Assert.Equal("DiagnosticsTestApp", env.ConfigurationEntries["App:Name"]);

        var assemblies = adapter.GetLoadedAssemblies();
        Assert.NotEmpty(assemblies);
        Assert.Contains(assemblies, a => a.Name.Contains("TemporaryDiagnostics"));
    }

    [Fact]
    public void RuntimeVitalsCollector_ShouldCollectProcessAndGcMetrics()
    {
        var collector = new RuntimeVitalsCollector();
        var vitals = collector.GetCurrentVitals();

        Assert.NotNull(vitals);
        Assert.True(vitals.ProcessId > 0);
        Assert.True(vitals.WorkingSetBytes > 0);
        Assert.True(vitals.ThreadPoolAvailableWorkers > 0);
        Assert.True(vitals.TotalAllocatedBytes > 0);
    }
}
