using TemporaryDiagnostics.AspNetCore.Extensions;
using TemporaryDiagnostics.SampleHost.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. 注册业务服务（用于动态反射与 Fast Emit 测试）
builder.Services.AddSingleton<IOrderService, OrderService>();
builder.Services.AddScoped<IInventoryCacheService, InventoryCacheService>();

// 2. 一键注册临时诊断中间件（仅需 AccessKey，RoutePrefix 可选）
builder.Services.AddTemporaryDiagnostics(options =>
{
    options.AccessKey = builder.Configuration["DIAG_KEY"] ?? "SecretDiagKey@2026";
    options.RoutePrefix = "/__diag"; // 默认就是 /__diag
    options.SessionTimeoutMinutes = 30;
    options.EnableTerminal = true;
    options.EnableDynamicInvoke = true;
    options.EnableMcp = true;
});

var app = builder.Build();

// 3. 一键启用临时诊断中间件与端点
app.UseTemporaryDiagnostics();

// 模拟普通业务 API
app.MapGet("/", () => Results.Ok(new
{
    service = "TemporaryDiagnostics.SampleHost",
    status = "Running",
    diagnosticsPortal = "/__diag",
    time = DateTime.UtcNow
}));

app.MapGet("/api/hello", (string? name) => Results.Ok(new
{
    message = $"Hello, {name ?? "World"}!",
    time = DateTime.UtcNow
}));

app.Run();

// 供 WebApplicationFactory 单元测试与集成测试引用
public partial class Program { }
