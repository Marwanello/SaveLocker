using SaveLocker.Server.Services;

namespace SaveLocker.Server;

/// <summary>
/// Turns the two ways a request can name a playthrough wrongly into the answers an agent or the console can tell apart
/// from "the server is broken": no such playthrough (404), or one that is archived and so frozen (409).
/// </summary>
public sealed class PlaythroughErrorFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { return await next(context); }
        catch (PlaythroughNotFoundException ex) { return Results.NotFound(ex.Message); }
        catch (PlaythroughArchivedException ex) { return Results.Conflict(ex.Message); }
    }
}
