namespace HackerNews.Api.Shared.Options;

/// <summary>
/// How the app sits behind a reverse proxy. Azure App Service terminates the connection at its
/// front end, so without this the only address Kestrel ever sees is the load balancer's &mdash;
/// which would drop every caller in the world into a single rate-limit bucket.
/// </summary>
public sealed class NetworkOptions
{
    public const string SectionName = "Network";

    /// <summary>
    /// Trust <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> to describe the caller. Off by
    /// default, and deliberately so: with no proxy in front, those headers are client-supplied,
    /// and trusting them would let anyone rotate a fake address per request and walk straight past
    /// the rate limiter. Turn it on only where a proxy you control really does sit in front.
    /// </summary>
    public bool TrustForwardedHeaders { get; set; }

    /// <summary>
    /// How many proxy hops sit in front of the app. Only this many entries are read, and they are
    /// read from the <em>right</em> of the header &mdash; the end a proxy appends to &mdash; so a
    /// value the client planted on the left is never mistaken for the caller.
    /// </summary>
    public int TrustedProxyHops { get; set; } = 1;
}
