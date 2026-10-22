using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TemporaryDiagnostics.Core.Contracts;
using TemporaryDiagnostics.Core.Models;
using TemporaryDiagnostics.Infrastructure.DynamicInvoke;
using TemporaryDiagnostics.SampleHost.Services;
using Xunit;

namespace TemporaryDiagnostics.Tests;

public class DynamicInvokeTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IDynamicInvoker _invoker;

    public DynamicInvokeTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOrderService, OrderService>();
        services.AddScoped<IInventoryCacheService, InventoryCacheService>();

        DiServiceInspector.SetServiceCollection(services);

        _serviceProvider = services.BuildServiceProvider();
        _invoker = new DynamicInvokeCoordinator();
    }

    [Fact]
    public void DiServiceInspector_ShouldDiscoverRegisteredServices()
    {
        var registered = _invoker.GetRegisteredServices(_serviceProvider);

        Assert.NotEmpty(registered);
        Assert.Contains(registered, s => s.ServiceType.Contains("IOrderService"));
        Assert.Contains(registered, s => s.ServiceType.Contains("IInventoryCacheService"));
    }

    [Fact]
    public void GetServiceMethods_ShouldReturnPublicMethods()
    {
        var result = _invoker.GetServiceMethods(typeof(IOrderService).FullName!);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Contains(result.Data, m => m.Name == "GetOrderStatus");
        Assert.Contains(result.Data, m => m.Name == "ProcessOrderAsync" && m.IsAsync);
    }

    [Fact]
    public async Task InvokeAsync_SyncMethod_WithFastEmit_ShouldSucceed()
    {
        var request = new DynamicInvokeRequest
        {
            ServiceTypeName = typeof(IOrderService).FullName!,
            MethodName = "GetOrderStatus",
            Parameters = new List<object?> { 8801 },
            UseFastEmit = true
        };

        var response = await _invoker.InvokeAsync(request, _serviceProvider);

        Assert.True(response.Success);
        Assert.NotNull(response.Result);
        Assert.Contains("订单 #8801", response.Result.ToString()!);
        Assert.Equal("EmitFastInvoker", response.StrategyUsed);
    }

    [Fact]
    public async Task InvokeAsync_AsyncTaskMethod_ShouldUnwrapResult()
    {
        var request = new DynamicInvokeRequest
        {
            ServiceTypeName = typeof(IOrderService).FullName!,
            MethodName = "ProcessOrderAsync",
            Parameters = new List<object?> { 9902, "张三" },
            UseFastEmit = true
        };

        var response = await _invoker.InvokeAsync(request, _serviceProvider);

        Assert.True(response.Success);
        Assert.NotNull(response.Result);
        Assert.Contains("张三", response.Result.ToString()!);
        Assert.Contains("9902", response.Result.ToString()!);
    }

    [Fact]
    public async Task InvokeAsync_ValueTaskMethod_ShouldUnwrapResult()
    {
        var request = new DynamicInvokeRequest
        {
            ServiceTypeName = typeof(IInventoryCacheService).FullName!,
            MethodName = "CheckAvailabilityAsync",
            Parameters = new List<object?> { "SKU-IPHONE16", 10 },
            UseFastEmit = true
        };

        var response = await _invoker.InvokeAsync(request, _serviceProvider);

        Assert.True(response.Success);
        Assert.NotNull(response.Result);
        Assert.Equal(true, response.Result);
    }

    [Fact]
    public async Task FastEmit_Benchmark_ShouldBeFast()
    {
        var request = new DynamicInvokeRequest
        {
            ServiceTypeName = typeof(IOrderService).FullName!,
            MethodName = "GetOrderStatus",
            Parameters = new List<object?> { 100 },
            UseFastEmit = true
        };

        // 预热
        await _invoker.InvokeAsync(request, _serviceProvider);

        var sw = Stopwatch.StartNew();
        const int iterations = 1000;
        for (var i = 0; i < iterations; i++)
        {
            await _invoker.InvokeAsync(request, _serviceProvider);
        }
        sw.Stop();

        var avgMicroseconds = (sw.Elapsed.TotalMilliseconds * 1000.0) / iterations;
        Assert.True(avgMicroseconds < 500, $"Fast Emit 单次平均调用耗时应低于 500 微秒，实际: {avgMicroseconds:F2} 微秒");
    }
}
