using System.Text.Json;
using Bigarr.Api.Features.Migration;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Tv;

namespace Bigarr.Api.Tests.Migration;

/// <summary>Radarr's and Sonarr's <c>mediaInfo</c> read into Bigarr's, named like a probe names it; what does not parse stays empty.</summary>
public sealed class ArrMediaInfoTests
{
    [Fact]
    public void A_sonarr_media_info_maps_like_a_probe()
    {
        var info = ArrMediaInfo.From(Parse("""
            {
              "audioBitrate": 640000, "audioChannels": 5.1, "audioCodec": "EAC3", "audioLanguages": "eng/fre",
              "audioStreamCount": 2, "videoBitDepth": 10, "videoBitrate": 8000000, "videoCodec": "x265",
              "videoDynamicRange": "HDR", "videoDynamicRangeType": "HDR10", "videoFps": 23.976,
              "resolution": "3840x2160", "runTime": "45:12", "scanType": "Progressive", "subtitles": "eng/ger/und"
            }
            """), "Show.S01E01.2160p.WEB-DL.x265-GRP")!;

        Assert.Equal("x265", info.VideoCodec);
        Assert.Equal(8_000_000, info.VideoBitrate);
        Assert.Equal(10, info.VideoBitDepth);
        Assert.Equal("HDR", info.VideoDynamicRange);
        Assert.Equal("HDR10", info.VideoDynamicRangeType);
        Assert.Equal(3840, info.Width);
        Assert.Equal(2160, info.Height);
        Assert.Equal("Progressive", info.ScanType);
        Assert.Equal("EAC3", info.AudioCodec);
        Assert.Equal(640_000, info.AudioBitrate);
        Assert.Equal(5.1, info.AudioChannels);
        Assert.Equal(["English", "French"], info.AudioLanguages);
        Assert.Equal(["English", "German"], info.Subtitles);
        Assert.Equal(new TimeSpan(0, 45, 12), info.RunTime);
        Assert.Null(info.ContainerFormat);
    }

    [Fact]
    public void Codec_names_come_out_as_a_probe_writes_them()
    {
        var info = ArrMediaInfo.From(Parse("""{ "videoCodec": "HEVC", "audioCodec": "dts-hd ma", "videoDynamicRange": "", "scanType": "interlaced" }"""), "Film.2020.1080p.BluRay.x265-GRP")!;

        Assert.Equal("x265", info.VideoCodec);
        Assert.Equal("DTS-HD MA", info.AudioCodec);
        Assert.Null(info.VideoDynamicRange);
        Assert.Equal("Interlaced", info.ScanType);
        Assert.Equal("h264", ArrMediaInfo.VideoCodec("H264", null));
        Assert.Equal("TrueHD Atmos", ArrMediaInfo.AudioCodec("truehd atmos"));
    }

    [Theory]
    [InlineData("1:45:12", 1, 45, 12)]
    [InlineData("45:12", 0, 45, 12)]
    [InlineData("02:03:04.567", 2, 3, 4)]
    [InlineData("0:59", 0, 0, 59)]
    public void A_run_time_parses(string value, int hours, int minutes, int seconds) =>
        Assert.Equal(new TimeSpan(hours, minutes, seconds), ArrMediaInfo.RunTime(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0:00")]
    [InlineData("45")]
    [InlineData("1:2:3:4")]
    [InlineData("1:61:00")]
    [InlineData("45:75")]
    [InlineData("-1:00")]
    [InlineData("ab:cd")]
    public void A_bad_run_time_gives_none(string? value) => Assert.Null(ArrMediaInfo.RunTime(value));

    [Theory]
    [InlineData("1920x1080", 1920, 1080)]
    [InlineData("720 x 576", 720, 576)]
    public void A_resolution_parses(string value, int width, int height) => Assert.Equal((width, height), ArrMediaInfo.Resolution(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1080p")]
    [InlineData("0x1080")]
    [InlineData("1920x")]
    [InlineData("1920x1080x3")]
    [InlineData("-1920x1080")]
    public void A_bad_resolution_gives_none(string? value) => Assert.Equal((null, null), ArrMediaInfo.Resolution(value));

    [Fact]
    public void Languages_accept_codes_and_names_and_drop_the_rest() =>
        Assert.Equal(["Japanese", "English", "French"], ArrMediaInfo.Languages("jpn / English/fra/und/xx-nope/zzz//eng"));

    [Fact]
    public void Garbage_values_are_dropped_one_by_one()
    {
        var info = ArrMediaInfo.From(Parse("""
            {
              "audioBitrate": "lots", "audioChannels": -2, "audioCodec": 42, "audioLanguages": ["eng"],
              "videoBitDepth": 0, "videoBitrate": -5, "videoCodec": "x264", "resolution": "big",
              "runTime": "soon", "scanType": "weird", "subtitles": 7
            }
            """), null)!;

        Assert.Equal("x264", info.VideoCodec);
        Assert.Null(info.AudioBitrate);
        Assert.Null(info.AudioChannels);
        Assert.Null(info.AudioCodec);
        Assert.Empty(info.AudioLanguages);
        Assert.Null(info.VideoBitDepth);
        Assert.Null(info.VideoBitrate);
        Assert.Null(info.Width);
        Assert.Null(info.Height);
        Assert.Null(info.RunTime);
        Assert.Null(info.ScanType);
        Assert.Empty(info.Subtitles);
    }

    [Fact]
    public void Nothing_usable_gives_no_media_info()
    {
        Assert.Null(ArrMediaInfo.From(null, null));
        Assert.Null(ArrMediaInfo.From(Parse("null"), null));
        Assert.Null(ArrMediaInfo.From(Parse("\"x264\""), null));
        Assert.Null(ArrMediaInfo.From(Parse("{}"), null));
        Assert.Null(ArrMediaInfo.From(Parse("""{ "resolution": "", "runTime": "", "audioLanguages": "" }"""), null));
    }

    [Fact]
    public void A_file_bigarr_has_probed_keeps_its_own_media_info()
    {
        var arr = Parse("""{ "videoCodec": "x265", "resolution": "3840x2160" }""");

        var probed = new MediaFile { RelativePath = "Film.mkv", MediaInfo = new MediaInfo { VideoCodec = "h264" } }.WithArrMediaInfo(arr);
        Assert.Equal("h264", probed.MediaInfo!.VideoCodec);
        Assert.False(probed.MediaInfoFromArr);

        var bare = new EpisodeFile { RelativePath = "Show.S01E01.mkv" }.WithArrMediaInfo(arr);
        Assert.Equal("x265", bare.MediaInfo!.VideoCodec);
        Assert.True(bare.MediaInfoFromArr);

        // The arr's own, from an earlier pass, follows what the arr says now.
        var again = bare.WithArrMediaInfo(Parse("""{ "videoCodec": "AV1" }"""));
        Assert.Equal("AV1", again.MediaInfo!.VideoCodec);

        // Nothing usable from the arr: what the file had stays.
        Assert.Equal("AV1", again.WithArrMediaInfo(Parse("{}")).MediaInfo!.VideoCodec);
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
