namespace Bigarr.Decision;

public enum RejectionType
{
    /// <summary>The release will never fit this target (wrong quality, blocklisted, constraint unmet).</summary>
    Permanent = 1,

    /// <summary>Might fit later (not enough seeders yet, a download is already running).</summary>
    Temporary = 2,
}

/// <summary>One reason a release was refused for a target. Shown as-is in the interactive search.</summary>
public sealed record Rejection(string Reason, RejectionType Type = RejectionType.Permanent);
