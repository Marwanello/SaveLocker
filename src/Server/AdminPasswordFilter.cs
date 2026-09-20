using SaveLocker.Server.Services;

namespace SaveLocker.Server;

/// <summary>
/// Endpoint filter that guards the admin dashboard API.
/// Passes through freely when no password has been configured yet (first-run open state).
/// Once a password is set, a request must carry EITHER a live console session
/// (<c>X-Admin-Session</c>, minted by <c>POST /api/admin/session</c>) OR — for scripts, and for
/// consoles from before sessions existed — the password itself in <c>X-Admin-Password</c>.
/// Password attempts are throttled (see <see cref="AuthThrottle"/>); session tokens are not, because
/// a wrong one is not a guess and an expired one must not lock its owner out of signing in again.
/// </summary>
public sealed class AdminPasswordFilter : IEndpointFilter
{
    public const string SessionHeader = "X-Admin-Session";
    public const string PasswordHeader = "X-Admin-Password";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        var settings = http.RequestServices.GetRequiredService<SettingsService>();

        var storedHash = await settings.GetEffectiveAsync(SettingsService.AdminPasswordHash);
        if (string.IsNullOrEmpty(storedHash))
            return await next(ctx);

        var auth = http.RequestServices.GetRequiredService<AdminAuth>();

        var session = http.Request.Headers[SessionHeader].FirstOrDefault();
        if (!string.IsNullOrEmpty(session) && await auth.ValidateSessionAsync(session))
            return await next(ctx);

        var provided = http.Request.Headers[PasswordHeader].FirstOrDefault();
        if (!string.IsNullOrEmpty(provided))
        {
            var check = await auth.CheckPasswordAsync(provided, storedHash, http, "header");
            if (check.Outcome == PasswordOutcome.Ok) return await next(ctx);
            if (check.Outcome == PasswordOutcome.Throttled) return AuthResults.TooManyAttempts(http, check.RetryAfter);
        }

        return Results.Unauthorized();
    }
}

public static class AuthResults
{
    /// <summary>429 with a <c>Retry-After</c> header, so a client can say how long to wait rather than guess.</summary>
    public static IResult TooManyAttempts(HttpContext http, TimeSpan? retryAfter)
    {
        var seconds = (int)Math.Ceiling(Math.Max(1, retryAfter?.TotalSeconds ?? 60));
        http.Response.Headers.RetryAfter = seconds.ToString();
        return Results.Json(
            new { error = $"Too many wrong passwords. Try again in {FormatWait(seconds)}." },
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    private static string FormatWait(int seconds) =>
        seconds < 90 ? $"{seconds} seconds" : $"{(seconds + 59) / 60} minutes";
}
