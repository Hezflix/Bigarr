using System.Text.Json.Serialization;

namespace Bigarr.Api.Infrastructure.Downloads.QBittorrent;

/// <summary>One entry of <c>GET /api/v2/torrents/info</c>. Snake case on the wire.</summary>
public sealed class QBittorrentTorrent
{
    public string Hash { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Selected size.</summary>
    public long Size { get; set; }

    [JsonPropertyName("total_size")]
    public long TotalSize { get; set; }

    /// <summary>0 to 1.</summary>
    public double Progress { get; set; }

    [JsonPropertyName("dlspeed")]
    public long DownloadSpeed { get; set; }

    /// <summary>Seconds; 8640000 means unknown.</summary>
    public long Eta { get; set; }

    public string State { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("save_path")]
    public string? SavePath { get; set; }

    [JsonPropertyName("content_path")]
    public string? ContentPath { get; set; }

    [JsonPropertyName("added_on")]
    public long AddedOn { get; set; }

    [JsonPropertyName("completion_on")]
    public long CompletionOn { get; set; }

    public double Ratio { get; set; }

    [JsonPropertyName("seeding_time")]
    public long SeedingTime { get; set; }

    [JsonPropertyName("amount_left")]
    public long AmountLeft { get; set; }

    [JsonPropertyName("num_seeds")]
    public int NumSeeds { get; set; }
}

public sealed class QBittorrentCategory
{
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("savePath")]
    public string? SavePath { get; set; }
}
