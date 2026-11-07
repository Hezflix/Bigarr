namespace Bigarr.Api.Features.Shadow;

/// <summary>
/// Bound from <c>Bigarr:Shadow</c> (env: <c>Bigarr__Shadow__Enabled=true</c>). Shadow mode is Bigarr running next to the
/// Radarrs it is meant to replace: every grab path (RSS pass, automatic and interactive search) records what it would
/// have sent instead of calling a download client, and the comparison page puts those picks next to Radarr's grabs.
/// </summary>
public sealed class ShadowOptions
{
    public const string Section = "Bigarr:Shadow";

    /// <summary>Record picks instead of grabbing. Nothing is ever sent to a download client while it is on.</summary>
    public bool Enabled { get; set; }
}
