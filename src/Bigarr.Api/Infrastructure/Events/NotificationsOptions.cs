namespace Bigarr.Api.Infrastructure.Events;

/// <summary>Bound from <c>Bigarr:Notifications</c> (env: <c>Bigarr__Notifications__TimeoutSeconds</c>…).</summary>
public sealed class NotificationsOptions
{
    public const string Section = "Bigarr:Notifications";

    /// <summary>Radarr-like escalation between two attempts at the same delivery; the last value repeats.</summary>
    public static readonly int[] DefaultRetryDelaysMinutes = [1, 5, 15, 60, 180, 360, 720, 1440];

    public bool Enabled { get; set; } = true;

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>After this many failed attempts a delivery is given up; a manual retry starts it over.</summary>
    public int MaxAttempts { get; set; } = 10;

    public int[] RetryDelaysMinutes { get; set; } = DefaultRetryDelaysMinutes;

    /// <summary>How often the sender looks for due deliveries when nothing woke it up.</summary>
    public int SweepSeconds { get; set; } = 60;

    /// <summary>Dispatched events (and their deliveries) older than this are purged by the maintenance job.</summary>
    public int RetentionDays { get; set; } = 14;

    /// <summary>The <c>applicationUrl</c> of Radarr-format payloads (Radarr's Settings → General → Application URL); empty like Radarr's default.</summary>
    public string? ApplicationUrl { get; set; } = string.Empty;

    public TimeSpan RetryDelay(int attempts)
    {
        var delays = RetryDelaysMinutes is { Length: > 0 } ? RetryDelaysMinutes : DefaultRetryDelaysMinutes;
        return TimeSpan.FromMinutes(Math.Max(0, delays[Math.Clamp(attempts - 1, 0, delays.Length - 1)]));
    }
}
