namespace Bigarr.Api.Infrastructure;

/// <summary>Bound from the <c>Bigarr</c> configuration section (env: <c>Bigarr__ConfigDirectory</c>…).</summary>
public sealed class BigarrOptions
{
    public const string Section = "Bigarr";

    /// <summary>Holds the SQLite database, backups and image cache. <c>/config</c> in Docker.</summary>
    public string ConfigDirectory { get; set; } = "config";

    /// <summary>Password of the seeded admin on first start. When absent a random one is generated and logged once.</summary>
    public string? InitialAdminPassword { get; set; }

    public string DatabasePath => Path.Combine(ConfigDirectory, "bigarr.db");
}
