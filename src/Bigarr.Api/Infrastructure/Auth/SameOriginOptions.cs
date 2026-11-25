namespace Bigarr.Api.Infrastructure.Auth;

/// <summary>Bound from <c>Bigarr:Auth</c> (env: <c>Bigarr__Auth__RequireSameOrigin</c>).</summary>
public sealed class SameOriginOptions
{
    public const string Section = "Bigarr:Auth";

    /// <summary>
    /// Refuse a request that changes something with the session cookie unless the browser says it comes from Bigarr's
    /// own pages. Turn off only behind a proxy that rewrites the Host header without forwarding the original one.
    /// </summary>
    public bool RequireSameOrigin { get; set; } = true;
}
