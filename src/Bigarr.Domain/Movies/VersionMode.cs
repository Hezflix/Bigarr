namespace Bigarr.Domain.Movies;

public enum VersionMode
{
    /// <summary>The profile drives it: RSS, searches, upgrades until cutoff.</summary>
    Managed = 1,

    /// <summary>Grabbed once, never upgraded; monitoring is switched off automatically at import.</summary>
    OneShot = 2,
}
