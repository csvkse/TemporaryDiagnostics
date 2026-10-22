using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TemporaryDiagnostics.AspNetCore.Extensions;
using TemporaryDiagnostics.AspNetCore.Middleware;
using TemporaryDiagnostics.Core.Models;
using TemporaryDiagnostics.SampleHost.Services;
using Xunit;

namespace TemporaryDiagnostics.Tests;

public class MiddlewareIntegrationTests
{
    private readonly TestServer _server;
    private readonly HttpClient _client;
    private const string AccessKey = "SuperSecretKey@2026";

    public MiddlewareIntegrationTests()
    {
        var hostBuilder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton<IOrderService, OrderService>();
                services.AddTemporaryDiagnostics(options =>
                {
                    options.AccessKey = AccessKey;
                    options.RoutePrefix = "/__diag";
                });
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseWebSockets();
                app.UseMiddleware<DiagnosticHttpCaptureMiddleware>();
                app.UseMiddleware<DiagnosticAuthMiddleware>();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapTemporaryDiagnostics();
                    endpoints.MapGet("/api/hello", () => "world");
                });
            });

        _server = new TestServer(hostBuilder);
        _client = _server.CreateClient();
    }

    [Fact]
    public async Task PublicBusinessApi_ShouldWorkWithoutAuth()
    {
        var response = await _client.GetAsync("/api/hello");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Equal("world", text);
    }

    [Fact]
    public async Task DiagProtectedEndpoint_WithoutAuth_ShouldReturn401()
    {
        var response = await _client.GetAsync("/__diag/api/system/overview");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DiagLogin_WithWrongKey_ShouldReturn401()
    {
        var response = await _client.PostAsJsonAsync("/__diag/api/auth/login", new { accessKey = "WrongKey" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DiagLogin_WithCorrectKey_ShouldReturnTokenAndSetCookie()
    {
        var response = await _client.PostAsJsonAsync("/__diag/api/auth/login", new { accessKey = AccessKey });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("isSuccess").GetBoolean());
        Assert.True(json.TryGetProperty("token", out var tokenProp));
        Assert.False(string.IsNullOrWhiteSpace(tokenProp.GetString()));

        // 验证 Set-Cookie
        Assert.True(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task DiagProtectedEndpoint_WithHeaderKey_ShouldReturn200()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/__diag/api/system/overview");
        request.Headers.Add("X-Diag-Key", AccessKey);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var overview = await response.Content.ReadFromJsonAsync<SystemOverview>();
        Assert.NotNull(overview);
        Assert.False(string.IsNullOrWhiteSpace(overview.MachineName));
    }

    [Fact]
    public async Task DiagProtectedEndpoint_WithBearerToken_ShouldReturn200()
    {
        // 1. 登录换取 Token
        var loginRes = await _client.PostAsJsonAsync("/__diag/api/auth/login", new { accessKey = AccessKey });
        var loginData = await loginRes.Content.ReadFromJsonAsync<JsonElement>();
        var token = loginData.GetProperty("token").GetString();

        // 2. 用 Bearer Token 访问
        using var request = new HttpRequestMessage(HttpMethod.Get, "/__diag/api/system/vitals");
        request.Headers.Add("Authorization", $"Bearer {token}");

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var vitals = await response.Content.ReadFromJsonAsync<RuntimeVitals>();
        Assert.NotNull(vitals);
        Assert.True(vitals.ProcessId > 0);
    }

    [Fact]
    public async Task DiagDocsAndOpenApi_ShouldBeAccessible()
    {
        // OpenAPI JSON
        using var reqJson = new HttpRequestMessage(HttpMethod.Get, "/__diag/openapi.json");
        reqJson.Headers.Add("X-Diag-Key", AccessKey);
        var resJson = await _client.SendAsync(reqJson);
        Assert.Equal(HttpStatusCode.OK, resJson.StatusCode);
        var openApiText = await resJson.Content.ReadAsStringAsync();
        Assert.Contains("openapi", openApiText);

        // Docs HTML
        using var reqDocs = new HttpRequestMessage(HttpMethod.Get, "/__diag/docs");
        reqDocs.Headers.Add("X-Diag-Key", AccessKey);
        var resDocs = await _client.SendAsync(reqDocs);
        Assert.Equal(HttpStatusCode.OK, resDocs.StatusCode);
        var docsHtml = await resDocs.Content.ReadAsStringAsync();
        Assert.Contains("TemporaryDiagnostics API", docsHtml);
    }

    [Fact]
    public async Task DynamicInvokeApi_ShouldExecuteMethod()
    {
        var invokeReq = new DynamicInvokeRequest
        {
            ServiceTypeName = typeof(IOrderService).FullName!,
            MethodName = "GetOrderStatus",
            Parameters = new List<object?> { 7777 },
            UseFastEmit = true
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/__diag/api/invoke/call")
        {
            Content = JsonContent.Create(invokeReq)
        };
        request.Headers.Add("X-Diag-Key", AccessKey);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<DynamicInvokeResponse>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Contains("订单 #7777", result.Result?.ToString() ?? "");
    }
}
