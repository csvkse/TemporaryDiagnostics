using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TemporaryDiagnostics.AspNetCore.Security;
using TemporaryDiagnostics.Core.Common;

namespace TemporaryDiagnostics.AspNetCore.Endpoints;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder group)
    {
        var authGroup = group.MapGroup("/api/auth");

        authGroup.MapPost("/login", (HttpContext context, LoginRequest request, DiagnosticSessionStore sessionStore) =>
        {
            var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            var token = sessionStore.AuthenticateKey(request.AccessKey ?? string.Empty, clientIp);

            if (token != null)
            {
                context.Response.Cookies.Append("__diag_token", token, new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Lax,
                    Secure = context.Request.IsHttps,
                    Expires = DateTimeOffset.UtcNow.AddMinutes(30)
                });

                return Results.Ok(new { isSuccess = true, token });
            }

            return Results.Json(Result.Unauthorized("访问密钥无效或登录过于频繁，已被暂时限制"), statusCode: 401);
        });

        authGroup.MapPost("/logout", (HttpContext context, DiagnosticSessionStore sessionStore) =>
        {
            if (context.Request.Cookies.TryGetValue("__diag_token", out var token) && token != null)
            {
                sessionStore.RevokeToken(token);
            }
            context.Response.Cookies.Delete("__diag_token");
            return Results.Ok(Result.Success());
        });

        authGroup.MapGet("/status", (HttpContext context, DiagnosticSessionStore sessionStore) =>
        {
            var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            var token = context.Request.Cookies["__diag_token"]
                        ?? (context.Request.Headers["X-Diag-Key"].ToString() is { Length: > 0 } k ? k : null);

            var isValid = sessionStore.ValidateKeyOrToken(token, clientIp);
            return Results.Ok(new { isAuthenticated = isValid });
        });

        return group;
    }

    public sealed class LoginRequest
    {
        public string? AccessKey { get; set; }
    }
}
