using System.Globalization;
using System.Text.Json;
using Bigarr.Api.Infrastructure.Media;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Tv;
using Bigarr.Parsing;

namespace Bigarr.Api.Features.Migration;

/// <summary>
/// Radarr's and Sonarr's <c>mediaInfo</c> (the same resource in both) turned into the <see cref="MediaInfo"/> Bigarr
/// keeps, named the way <see cref="FfprobeOutputParser"/> names it. It only stands in until Bigarr's own probe reads the
/// file (<see cref="MediaFile.MediaInfoFromArr"/>). A field that is missing, of the wrong type or does not parse is left
/// empty; a resource with nothing usable gives no media info at all.
/// </summary>
public static class ArrMediaInfo
{
    private static readonly string[] VideoCodecs = ["x264", "h264", "x265", "h265", "AV1", "MPEG2", "XviD", "DivX", "VC1", "VP9", "WMV", "ProRes"];

    private static readonly string[] AudioCodecs =
    [
        "AAC", "AC3", "EAC3", "EAC3 Atmos", "DTS", "DTS-HD MA", "DTS-HD HRA", "DTS-X", "DTS-ES", "TrueHD", "TrueHD Atmos", "FLAC", "Opus", "MP3", "MP2",
        "Vorbis", "ALAC", "WMA", "PCM",
    ];

    public static MediaInfo? From(JsonElement? resource, string? releaseTitle)
    {
        if (resource is not { ValueKind: JsonValueKind.Object } r)
        {
            return null;
        }

        var (width, height) = Resolution(Str(r, "resolution"));
        var info = new MediaInfo
        {
            VideoCodec = VideoCodec(Str(r, "videoCodec"), releaseTitle),
            VideoBitrate = Positive(Long(r, "videoBitrate")),
            VideoBitDepth = Positive(Long(r, "videoBitDepth")) is { } depth and <= 64 ? (int)depth : null,
            VideoDynamicRange = Str(r, "videoDynamicRange"),
            VideoDynamicRangeType = Str(r, "videoDynamicRangeType"),
            Width = width,
            Height = height,
            ScanType = ScanType(Str(r, "scanType")),
            AudioCodec = AudioCodec(Str(r, "audioCodec")),
            AudioBitrate = Positive(Long(r, "audioBitrate")),
            AudioChannels = Dbl(r, "audioChannels") is { } channels and > 0 and < 100 ? Math.Round(channels, 1) : null,
            AudioLanguages = Languages(Str(r, "audioLanguages")),
            Subtitles = Languages(Str(r, "subtitles")),
            RunTime = RunTime(Str(r, "runTime")),
        };

        var empty = info.VideoCodec is null && info.Width is null && info.AudioCodec is null && info.AudioChannels is null && info.RunTime is null
                    && info.VideoBitrate is null && info.AudioLanguages.Count == 0 && info.Subtitles.Count == 0;
        return empty ? null : info;
    }

    /// <summary>
    /// The *arr's media info goes on a film file Bigarr has not read yet (none, or the *arr's from an earlier pass); one
    /// Bigarr's probe wrote is never replaced from here.
    /// </summary>
    public static MediaFile WithArrMediaInfo(this MediaFile file, JsonElement? resource)
    {
        ArgumentNullException.ThrowIfNull(file);
        if ((file.MediaInfo is null || file.MediaInfoFromArr) && From(resource, file.OriginalReleaseTitle) is { } info)
        {
            file.MediaInfo = info;
            file.MediaInfoFromArr = true;
        }

        return file;
    }

    /// <summary>The episode twin of <see cref="WithArrMediaInfo(MediaFile, JsonElement?)"/>.</summary>
    public static EpisodeFile WithArrMediaInfo(this EpisodeFile file, JsonElement? resource)
    {
        ArgumentNullException.ThrowIfNull(file);
        if ((file.MediaInfo is null || file.MediaInfoFromArr) && From(resource, file.OriginalReleaseTitle) is { } info)
        {
            file.MediaInfo = info;
            file.MediaInfoFromArr = true;
        }

        return file;
    }

    /// <summary>The *arrs already name codecs like Bigarr does; a raw ffprobe name (<c>hevc</c>) goes through the same mapping as a probe.</summary>
    public static string? VideoCodec(string? value, string? releaseTitle)
    {
        if (value is null)
        {
            return null;
        }

        var known = Array.Find(VideoCodecs, c => c.Equals(value, StringComparison.OrdinalIgnoreCase));
        return known ?? FfprobeOutputParser.VideoCodec(value.ToLowerInvariant(), null, releaseTitle);
    }

    public static string? AudioCodec(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var known = Array.Find(AudioCodecs, c => c.Equals(value, StringComparison.OrdinalIgnoreCase));
        return known ?? FfprobeOutputParser.AudioCodec(value.ToLowerInvariant(), null);
    }

    /// <summary><c>1920x1080</c>; anything else, or a zero side, gives no size.</summary>
    public static (int? Width, int? Height) Resolution(string? value)
    {
        var parts = value?.Split('x', StringSplitOptions.TrimEntries);
        if (parts is not { Length: 2 }
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var width)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var height)
            || width <= 0 || height <= 0)
        {
            return (null, null);
        }

        return (width, height);
    }

    /// <summary><c>45:12</c> (minutes and seconds) or <c>1:45:12</c>; seconds may carry a fraction. Zero or garbage gives nothing.</summary>
    public static TimeSpan? RunTime(string? value)
    {
        var parts = value?.Split(':', StringSplitOptions.TrimEntries);
        if (parts is not { Length: 2 or 3 })
        {
            return null;
        }

        var hours = 0;
        if (parts.Length == 3 && !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out hours))
        {
            return null;
        }

        if (!int.TryParse(parts[^2], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || !double.TryParse(parts[^1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds)
            || seconds >= 60 || (parts.Length == 3 && minutes >= 60))
        {
            return null;
        }

        var runTime = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(Math.Floor(seconds));
        return runTime > TimeSpan.Zero ? runTime : null;
    }

    /// <summary><c>eng/fre</c> as the *arrs join them; an English name is accepted too. Language names, like a probe gives.</summary>
    public static List<string> Languages(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(Language)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string? Language(string code)
    {
        if (code.ToLowerInvariant() is "und" or "zxx" or "mis" or "unknown")
        {
            return null;
        }

        var iso = code.Length is 2 or 3 || code.Contains('-', StringComparison.Ordinal) ? IsoLanguages.Find(code) : null;
        return (iso ?? IsoLanguages.FindByName(code))?.Language?.Name;
    }

    private static string? ScanType(string? value) => value?.ToLowerInvariant() switch
    {
        "progressive" => "Progressive",
        "interlaced" or "mbaff" or "paff" => "Interlaced",
        _ => null,
    };

    private static long? Positive(long? value) => value is > 0 ? value : null;

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()!.Trim() : null;

    private static long? Long(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var n) => n,
            JsonValueKind.Number when value.TryGetDouble(out var d) && d is >= long.MinValue and <= long.MaxValue => (long)d,
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
