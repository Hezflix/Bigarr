namespace Bigarr.Api.Features.Notifications.NewEpisodes;

/// <summary>Bound from <c>Bigarr:NewEpisodes</c>. The bot token itself is a setting of the instance, set in the UI.</summary>
public sealed class NewEpisodeOptions
{
    public const string Section = "Bigarr:NewEpisodes";

    /// <summary>A show's batch closes this long after its last episode landed: a season pack makes one message.</summary>
    public int DebounceSeconds { get; set; } = 120;

    /// <summary>A batch takes no new episode after this long, however they keep coming: the message is not held for ever.</summary>
    public int MaxBatchMinutes { get; set; } = 30;

    /// <summary>After the batch closes, how long to wait for Plex to list the episodes before telling anyway.</summary>
    public int PlexWaitMinutes { get; set; } = 30;

    /// <summary>How often the announcer looks at the batches and the messages due.</summary>
    public int SweepSeconds { get; set; } = 30;

    /// <summary>Discord's REST API. Changed by tests only.</summary>
    public string DiscordApiUrl { get; set; } = "https://discord.com/api/v10";

    public TimeSpan Debounce => TimeSpan.FromSeconds(Math.Max(0, DebounceSeconds));

    public TimeSpan MaxBatch => TimeSpan.FromMinutes(Math.Max(0, MaxBatchMinutes));

    public TimeSpan PlexWait => TimeSpan.FromMinutes(Math.Max(0, PlexWaitMinutes));

    public TimeSpan Sweep => TimeSpan.FromSeconds(Math.Max(1, SweepSeconds));
}
