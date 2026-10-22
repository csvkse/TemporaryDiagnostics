using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using TemporaryDiagnostics.Core.Common;
using TemporaryDiagnostics.Core.Contracts;

namespace TemporaryDiagnostics.AspNetCore.Endpoints;

public static class LogEndpoints
{
    public static RouteGroupBuilder MapLogEndpoints(this RouteGroupBuilder group)
    {
        var logGroup = group.MapGroup("/api/logs");

        logGroup.MapGet("/recent", ([FromQuery] int? limit, IDiagnosticLogSink sink) =>
        {
            var logs = sink.GetRecentLogs(limit ?? 100);
            return Results.Ok(logs);
        });

        logGroup.MapGet("/requests", ([FromQuery] int? limit, IDiagnosticLogSink sink) =>
        {
            var requests = sink.GetRecentHttpRequests(limit ?? 100);
            return Results.Ok(requests);
        });

        logGroup.MapGet("/exceptions", ([FromQuery] int? limit, IDiagnosticLogSink sink) =>
        {
            var exceptions = sink.GetRecentExceptions(limit ?? 100);
            return Results.Ok(exceptions);
        });

        logGroup.MapPost("/clear", (IDiagnosticLogSink sink) =>
        {
            sink.Clear();
            return Results.Ok(Result.Success());
        });

        return group;
    }
}
