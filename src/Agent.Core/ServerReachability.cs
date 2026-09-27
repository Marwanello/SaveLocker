using System.Net;
using System.Net.Sockets;

namespace SaveLocker.Agent;

/// <summary>
/// "Did the SaveLocker server answer?" — not the same question as "did anything answer". Behind a
/// reverse proxy (nginx, Caddy, Traefik) or Cloudflare, a server that is down still gets a response
/// back to the agent: the proxy's own 502/503/504, or Cloudflare's 52x. Read as the server's answer
/// that is a refusal — a push reported as failed instead of queued, and an outage the five-minute
/// notice never hears about. It is the server being gone.
/// <para>
/// A 401 from a revoked key or a 500 from the server itself is the opposite: a server that is very
/// much there, and calling it unreachable would be a false alarm about the wrong problem.
/// </para>
/// </summary>
public static class ServerReachability
{
    /// <summary>A status only something standing in front of the server sends, when the server
    /// behind it did not answer: bad gateway, unavailable, gateway timeout, and Cloudflare's 520–530
    /// ("web server is down", "origin unreachable", "a timeout occurred", …).</summary>
    public static bool IsGatewayFailure(HttpStatusCode? status) =>
        (int?)status is 502 or 503 or 504 or (>= 520 and <= 530);

    /// <summary>The server never answered: no response at all (<c>StatusCode</c> is null exactly then),
    /// a timeout — which surfaces as a cancellation rather than an HTTP failure — or a gateway
    /// answering in its place.</summary>
    public static bool IsUnreachable(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException { StatusCode: var status } => IsGatewayFailure(status),
        TaskCanceledException => true,
        _ => ex.InnerException is SocketException,
    };
}
