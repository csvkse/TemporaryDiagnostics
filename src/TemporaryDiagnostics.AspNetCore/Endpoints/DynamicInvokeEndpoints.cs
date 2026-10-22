using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using TemporaryDiagnostics.Core.Contracts;
using TemporaryDiagnostics.Core.Models;

namespace TemporaryDiagnostics.AspNetCore.Endpoints;

public static class DynamicInvokeEndpoints
{
    public static RouteGroupBuilder MapDynamicInvokeEndpoints(this RouteGroupBuilder group)
    {
        var invokeGroup = group.MapGroup("/api/invoke");

        invokeGroup.MapGet("/services", (IServiceProvider sp, IDynamicInvoker invoker) =>
        {
            var services = invoker.GetRegisteredServices(sp);
            return Results.Ok(services);
        });

        invokeGroup.MapGet("/methods", ([FromQuery] string serviceTypeName, IDynamicInvoker invoker) =>
        {
            var result = invoker.GetServiceMethods(serviceTypeName);
            return result.IsSuccess ? Results.Ok(result.Data) : Results.Json(result, statusCode: result.StatusCode);
        });

        invokeGroup.MapPost("/call", async (
            [FromBody] DynamicInvokeRequest request,
            IServiceProvider sp,
            IDynamicInvoker invoker,
            CancellationToken ct) =>
        {
            var response = await invoker.InvokeAsync(request, sp, ct);
            return Results.Ok(response);
        });

        return group;
    }
}
