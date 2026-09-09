namespace Bigarr.Domain.Acquisition;

/// <summary>Lifecycle of a tracked download (blueprint §3.5).</summary>
public enum GrabState
{
    /// <summary>Sent to the download client.</summary>
    Grabbed = 1,

    /// <summary>Seen in the download client.</summary>
    Downloading = 2,

    /// <summary>Completed on the client side, waiting for import.</summary>
    ImportPending = 3,

    Importing = 4,

    Imported = 5,

    /// <summary>Import needs a human: unreadable file, no space, no media, ambiguous match. Never blocklisted.</summary>
    ImportFailed = 6,

    /// <summary>The client reported an error or the download stalled: blocklist + search again.</summary>
    DownloadFailed = 7,

    /// <summary>Vanished from the client: terminal, no blocklist, the target is missing again.</summary>
    Removed = 8,

    /// <summary>Dismissed by the user.</summary>
    Ignored = 9,
}

public static class GrabStateExtensions
{
    public static bool IsActive(this GrabState state) =>
        state is GrabState.Grabbed or GrabState.Downloading or GrabState.ImportPending or GrabState.Importing;

    public static bool IsTerminal(this GrabState state) =>
        state is GrabState.Imported or GrabState.DownloadFailed or GrabState.Removed or GrabState.Ignored;
}
