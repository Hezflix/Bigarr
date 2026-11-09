namespace Bigarr.Domain.Playback;

public enum StreamDecision
{
    DirectPlay = 1,

    /// <summary>Remuxed: the container changes, video and audio are copied.</summary>
    DirectStream = 2,

    Transcode = 3,
}

/// <summary>
/// What a stream looked like, as the media server reported it: who played it on what, over which network, with which
/// decision, from which file and formats to which. Every field is optional: servers and imports fill what they know.
/// </summary>
public sealed class StreamDetails
{
    public string? PlayerTitle { get; set; }

    public string? PlayerProduct { get; set; }

    public string? PlayerPlatform { get; set; }

    public string? PlayerPlatformVersion { get; set; }

    public string? PlayerDevice { get; set; }

    /// <summary>The player's own id (Plex: <c>machineIdentifier</c>).</summary>
    public string? PlayerId { get; set; }

    public string? IpAddress { get; set; }

    /// <summary>On the server's local network.</summary>
    public bool? Local { get; set; }

    public bool? Secure { get; set; }

    /// <summary>Through the media server's relay instead of a direct connection.</summary>
    public bool? Relayed { get; set; }

    /// <summary>Kbps.</summary>
    public int? Bandwidth { get; set; }

    public StreamDecision? Decision { get; set; }

    /// <summary><c>directplay</c>, <c>copy</c> or <c>transcode</c>, as the server says it.</summary>
    public string? VideoDecision { get; set; }

    public string? AudioDecision { get; set; }

    public string? SubtitleDecision { get; set; }

    public bool? HardwareTranscode { get; set; }

    public string? SourceContainer { get; set; }

    /// <summary><c>4k</c>, <c>1080</c>, <c>720</c>, <c>sd</c>…</summary>
    public string? SourceVideoResolution { get; set; }

    public string? SourceVideoCodec { get; set; }

    /// <summary><c>SDR</c>, <c>HDR10</c>, <c>HLG</c>, <c>DV</c>.</summary>
    public string? SourceDynamicRange { get; set; }

    public string? SourceAudioCodec { get; set; }

    public int? SourceAudioChannels { get; set; }

    /// <summary>Kbps.</summary>
    public int? SourceBitrate { get; set; }

    public string? StreamContainer { get; set; }

    public string? StreamVideoResolution { get; set; }

    public string? StreamVideoCodec { get; set; }

    public string? StreamAudioCodec { get; set; }

    public int? StreamAudioChannels { get; set; }

    /// <summary>Kbps.</summary>
    public int? StreamBitrate { get; set; }

    /// <summary>The played file as the media server sees it.</summary>
    public string? ServerFilePath { get; set; }

    public StreamDetails Copy() => (StreamDetails)MemberwiseClone();
}
