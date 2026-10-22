using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TemporaryDiagnostics.Core.Contracts;

namespace TemporaryDiagnostics.AspNetCore.Endpoints;

public static class SystemEndpoints
{
    public static RouteGroupBuilder MapSystemEndpoints(this RouteGroupBuilder group)
    {
        var sysGroup = group.MapGroup("/api/system");

        sysGroup.MapGet("/overview", async (ISystemInfoAdapter adapter, CancellationToken ct) =>
        {
            var overview = await adapter.GetSystemOverviewAsync(ct);
            return Results.Ok(overview);
        });

        sysGroup.MapGet("/vitals", (IRuntimeVitalsCollector vitalsCollector) =>
        {
            var vitals = vitalsCollector.GetCurrentVitals();
            return Results.Ok(vitals);
        });

        sysGroup.MapGet("/environment", async (ISystemInfoAdapter adapter, CancellationToken ct) =>
        {
            var env = await adapter.GetEnvironmentInfoAsync(ct);
            return Results.Ok(env);
        });

        sysGroup.MapGet("/assemblies", (ISystemInfoAdapter adapter) =>
        {
            var assemblies = adapter.GetLoadedAssemblies();
            return Results.Ok(assemblies);
        });

        return group;
    }
}
