namespace Bigarr.Api.Infrastructure.Xem;

/// <summary>Bound from <c>Bigarr:Xem</c>. TheXEM is public and keyless; off, scene numbering simply stays empty.</summary>
public sealed class XemOptions
{
    public const string Section = "Bigarr:Xem";

    public bool Enabled { get; set; } = true;

    public string BaseUrl { get; set; } = "https://thexem.info/map/";

    /// <summary>How often the scene names and every series' mapping are refreshed in the background.</summary>
    public int RefreshIntervalHours { get; set; } = 12;

    public int TimeoutSeconds { get; set; } = 20;
}
