using System.Globalization;
using System.Text.Json;
using Bigarr.Domain.Movies;
using Bigarr.Parsing;

namespace Bigarr.Api.Infrastructure.Media;

/// <summary>Turns <c>ffprobe -show_format -show_streams</c> JSON into the <see cref="MediaInfo"/> subset Bigarr keeps, named the Radarr way.</summary>
public static class FfprobeOutputParser
{
    public static MediaInfo? Parse(string json, string? releaseTitle)
    {
        ArgumentNullException.ThrowIfNull(json);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        JsonElement? video = null;
        var audio = new List<JsonElement>();
        var subtitles = new List<JsonElement>();
        foreach (var stream in streams.EnumerateArray())
        {
            switch (Str(stream, "codec_type"))
            {
                case "video" when video is null && !IsAttachedPicture(stream):
                    video = stream;
                    break;
                case "audio":
                    audio.Add(stream);
                    break;
                case "subtitle":
                    subtitles.Add(stream);
                    break;
            }
        }

        if (video is null)
        {
            return null;
        }

        var info = new MediaInfo();
        if (root.TryGetProperty("format", out var format))
        {
            info.ContainerFormat = Container(Str(format, "format_name"));
            var duration = Dbl(format, "duration");
            if (duration is > 0)
            {
                info.RunTime = TimeSpan.FromSeconds(duration.Value);
            }
        }

        var v = video.Value;
        info.VideoCodec = VideoCodec(Str(v, "codec_name"), Str(v, "profile"), releaseTitle);
        info.Width = Int(v, "width");
        info.Height = Int(v, "height");
        info.VideoBitrate = Long(v, "bit_rate");
        info.VideoBitDepth = BitDepth(v);
        info.ScanType = Str(v, "field_order") is null or "progressive" or "unknown" ? "Progressive" : "Interlaced";
        (info.VideoDynamicRange, info.VideoDynamicRangeType) = DynamicRange(v);

        if (audio.Count > 0)
        {
            // The first audio stream is the main one, like Radarr assumes.
            var a = audio[0];
            info.AudioCodec = AudioCodec(Str(a, "codec_name"), Str(a, "profile"));
            info.AudioBitrate = Long(a, "bit_rate");
            info.AudioChannels = Channels(Str(a, "channel_layout"), Int(a, "channels"));
            info.AudioLanguages = audio.Select(LanguageOf).Where(l => l is not null).Select(l => l!).Distinct().ToList();
        }

        info.Subtitles = subtitles.Select(LanguageOf).Where(l => l is not null).Select(l => l!).Distinct().ToList();
        return info;
    }

    private static bool IsAttachedPicture(JsonElement stream) =>
        stream.TryGetProperty("disposition", out var disposition) && disposition.TryGetProperty("attached_pic", out var pic) && pic.ValueKind == JsonValueKind.Number && pic.GetInt32() == 1;

    private static string? Container(string? formatName) => formatName switch
    {
        null => null,
        var f when f.Contains("matroska", StringComparison.OrdinalIgnoreCase) => "Matroska",
        var f when f.Contains("mp4", StringComparison.OrdinalIgnoreCase) => "MP4",
        var f when f.Contains("avi", StringComparison.OrdinalIgnoreCase) => "AVI",
        var f when f.Contains("mpegts", StringComparison.OrdinalIgnoreCase) => "MPEG-TS",
        var f when f.Contains("asf", StringComparison.OrdinalIgnoreCase) => "WMV",
        _ => formatName.Split(',')[0].ToUpperInvariant(),
    };

    /// <summary>Radarr's convention: an encoder is x264/x265 unless the release says it is the codec (H.264, AVC, HEVC).</summary>
    public static string? VideoCodec(string? codecName, string? profile, string? releaseTitle)
    {
        var title = releaseTitle ?? string.Empty;
        return codecName?.ToLowerInvariant() switch
        {
            null => null,
            "h264" => title.Contains("h264", StringComparison.OrdinalIgnoreCase) || title.Contains("h.264", StringComparison.OrdinalIgnoreCase) || title.Contains("avc", StringComparison.OrdinalIgnoreCase) ? "h264" : "x264",
            "hevc" => title.Contains("h265", StringComparison.OrdinalIgnoreCase) || title.Contains("h.265", StringComparison.OrdinalIgnoreCase) || title.Contains("hevc", StringComparison.OrdinalIgnoreCase) ? "h265" : "x265",
            "av1" => "AV1",
            "mpeg2video" => "MPEG2",
            "mpeg4" => title.Contains("divx", StringComparison.OrdinalIgnoreCase) ? "DivX" : "XviD",
            "vc1" => "VC1",
            "vp9" => "VP9",
            "wmv3" or "wmv2" or "wmv1" => "WMV",
            "prores" => "ProRes",
            _ => codecName.ToUpperInvariant(),
        };
    }

    public static string? AudioCodec(string? codecName, string? profile)
    {
        var p = profile ?? string.Empty;
        return codecName?.ToLowerInvariant() switch
        {
            null => null,
            "aac" => "AAC",
            "ac3" => "AC3",
            "eac3" => p.Contains("Atmos", StringComparison.OrdinalIgnoreCase) || p.Contains("JOC", StringComparison.OrdinalIgnoreCase) ? "EAC3 Atmos" : "EAC3",
            "dts" => p.Contains("DTS-HD MA", StringComparison.OrdinalIgnoreCase) ? "DTS-HD MA"
                : p.Contains("DTS-HD HRA", StringComparison.OrdinalIgnoreCase) ? "DTS-HD HRA"
                : p.Contains("DTS:X", StringComparison.OrdinalIgnoreCase) || p.Contains("DTS-X", StringComparison.OrdinalIgnoreCase) ? "DTS-X"
                : p.Contains("DTS-ES", StringComparison.OrdinalIgnoreCase) ? "DTS-ES"
                : "DTS",
            "truehd" => p.Contains("Atmos", StringComparison.OrdinalIgnoreCase) ? "TrueHD Atmos" : "TrueHD",
            "flac" => "FLAC",
            "opus" => "Opus",
            "mp3" => "MP3",
            "mp2" => "MP2",
            "vorbis" => "Vorbis",
            "alac" => "ALAC",
            "wmav1" or "wmav2" or "wmapro" => "WMA",
            var c when c.StartsWith("pcm_", StringComparison.Ordinal) => "PCM",
            _ => codecName.ToUpperInvariant(),
        };
    }

    public static double? Channels(string? layout, int? count)
    {
        if (!string.IsNullOrWhiteSpace(layout))
        {
            var head = layout.Split('(')[0].Trim();
            switch (head.ToLowerInvariant())
            {
                case "mono":
                    return 1.0;
                case "stereo":
                    return 2.0;
                case "quad":
                    return 4.0;
            }

            var parts = head.Split('.');
            if (parts.Length == 2 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var main) && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var lfe))
            {
                return main + (lfe / 10.0);
            }
        }

        return count switch
        {
            null or <= 0 => null,
            1 => 1.0,
            2 => 2.0,
            var n => (n - 1) + 0.1,
        };
    }

    private static int? BitDepth(JsonElement video)
    {
        var raw = Str(video, "bits_per_raw_sample");
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bits) && bits > 0)
        {
            return bits;
        }

        var pixelFormat = Str(video, "pix_fmt") ?? string.Empty;
        if (pixelFormat.Contains("p16", StringComparison.Ordinal))
        {
            return 16;
        }

        if (pixelFormat.Contains("p12", StringComparison.Ordinal))
        {
            return 12;
        }

        if (pixelFormat.Contains("p10", StringComparison.Ordinal))
        {
            return 10;
        }

        return pixelFormat.Length == 0 ? null : 8;
    }

    private static (string? Range, string? Type) DynamicRange(JsonElement video)
    {
        var transfer = Str(video, "color_transfer")?.ToLowerInvariant();
        var pq = transfer is "smpte2084";
        var hlg = transfer is "arib-std-b67";
        var dolbyVision = false;
        var hdr10Plus = false;
        var mastering = false;
        if (video.TryGetProperty("side_data_list", out var sideData) && sideData.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in sideData.EnumerateArray())
            {
                var kind = Str(item, "side_data_type") ?? string.Empty;
                dolbyVision |= kind.Contains("DOVI", StringComparison.OrdinalIgnoreCase) || kind.Contains("Dolby Vision", StringComparison.OrdinalIgnoreCase);
                hdr10Plus |= kind.Contains("SMPTE2094-40", StringComparison.OrdinalIgnoreCase) || kind.Contains("HDR10+", StringComparison.OrdinalIgnoreCase);
                mastering |= kind.Contains("Mastering display", StringComparison.OrdinalIgnoreCase) || kind.Contains("Content light level", StringComparison.OrdinalIgnoreCase);
            }
        }

        string? type = dolbyVision switch
        {
            true when hdr10Plus => "DV HDR10+",
            true when pq => "DV HDR10",
            true when hlg => "DV HLG",
            true => "DV",
            _ when hdr10Plus => "HDR10+",
            _ when pq => mastering ? "HDR10" : "PQ",
            _ when hlg => "HLG",
            _ => null,
        };
        return (type is null ? null : "HDR", type);
    }

    private static string? LanguageOf(JsonElement stream)
    {
        if (!stream.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var code = Str(tags, "language");
        if (string.IsNullOrWhiteSpace(code) || code is "und" or "zxx" or "mis")
        {
            return null;
        }

        return IsoLanguages.Find(code)?.Language?.Name;
    }

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Int(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) => s,
            _ => null,
        };
    }

    private static long? Long(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var n) => n,
            JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) => s,
            _ => null,
        };
    }

    private static double? Dbl(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) => s,
            _ => null,
        };
    }
}
