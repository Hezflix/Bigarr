using Bigarr.Api.Infrastructure.Media;

namespace Bigarr.Api.Tests.Import;

public sealed class FfprobeOutputParserTests
{
    private const string Hdr10Remux = """
        {
          "streams": [
            { "index": 0, "codec_name": "hevc", "profile": "Main 10", "codec_type": "video", "width": 3840, "height": 2160, "pix_fmt": "yuv420p10le",
              "color_transfer": "smpte2084", "color_primaries": "bt2020", "field_order": "progressive",
              "side_data_list": [ { "side_data_type": "Mastering display metadata" }, { "side_data_type": "Content light level metadata" } ],
              "tags": { "language": "eng" } },
            { "index": 1, "codec_name": "truehd", "profile": "Dolby TrueHD + Dolby Atmos", "codec_type": "audio", "channels": 8, "channel_layout": "7.1", "tags": { "language": "eng" } },
            { "index": 2, "codec_name": "ac3", "codec_type": "audio", "channels": 6, "channel_layout": "5.1(side)", "bit_rate": "640000", "tags": { "language": "fre" } },
            { "index": 3, "codec_name": "subrip", "codec_type": "subtitle", "tags": { "language": "eng" } },
            { "index": 4, "codec_name": "subrip", "codec_type": "subtitle", "tags": { "language": "fra" } },
            { "index": 5, "codec_name": "mjpeg", "codec_type": "video", "disposition": { "attached_pic": 1 } }
          ],
          "format": { "format_name": "matroska,webm", "duration": "8160.512000", "bit_rate": "58000000" }
        }
        """;

    [Fact]
    public void Reads_codecs_channels_hdr_and_languages_the_radarr_way()
    {
        var info = FfprobeOutputParser.Parse(Hdr10Remux, "The.Matrix.1999.2160p.UHD.BluRay.REMUX.HDR.HEVC.TrueHD.Atmos-FraMeSToR")!;

        Assert.Equal("Matroska", info.ContainerFormat);
        Assert.Equal("h265", info.VideoCodec); // the release says HEVC: not an x265 encode
        Assert.Equal(3840, info.Width);
        Assert.Equal(2160, info.Height);
        Assert.Equal(10, info.VideoBitDepth);
        Assert.Equal("HDR", info.VideoDynamicRange);
        Assert.Equal("HDR10", info.VideoDynamicRangeType);
        Assert.Equal("Progressive", info.ScanType);
        Assert.Equal("TrueHD Atmos", info.AudioCodec);
        Assert.Equal(7.1, info.AudioChannels);
        Assert.Equal(["English", "French"], info.AudioLanguages);
        Assert.Equal(["English", "French"], info.Subtitles);
        Assert.InRange(info.RunTime!.Value.TotalSeconds, 8160.5, 8160.6);
    }

    [Fact]
    public void Dolby_vision_and_x264_defaults()
    {
        const string json = """
            { "streams": [
                { "codec_name": "hevc", "codec_type": "video", "width": 1920, "height": 1080, "pix_fmt": "yuv420p10le", "color_transfer": "smpte2084",
                  "side_data_list": [ { "side_data_type": "DOVI configuration record" } ] },
                { "codec_name": "eac3", "profile": "Dolby Digital Plus + Dolby Atmos", "codec_type": "audio", "channels": 6 },
                { "codec_name": "h264", "codec_type": "video" } ],
              "format": { "format_name": "mov,mp4,m4a,3gp,3g2,mj2" } }
            """;
        var info = FfprobeOutputParser.Parse(json, "Movie.2020.1080p.WEB-DL.DDP5.1.Atmos.DV.x265-GRP")!;
        Assert.Equal("MP4", info.ContainerFormat);
        Assert.Equal("x265", info.VideoCodec);
        Assert.Equal("DV HDR10", info.VideoDynamicRangeType);
        Assert.Equal("EAC3 Atmos", info.AudioCodec);
        Assert.Equal(5.1, info.AudioChannels);
        Assert.Empty(info.AudioLanguages);

        Assert.Equal("x264", FfprobeOutputParser.VideoCodec("h264", null, "Movie.2020.1080p.BluRay.x264-GRP"));
        Assert.Equal("h264", FfprobeOutputParser.VideoCodec("h264", null, "Movie.2020.1080p.WEB-DL.H.264-GRP"));
        Assert.Equal("DTS-HD MA", FfprobeOutputParser.AudioCodec("dts", "DTS-HD MA"));
        Assert.Equal(2.0, FfprobeOutputParser.Channels("stereo", 2));
        Assert.Equal(5.1, FfprobeOutputParser.Channels(null, 6));
    }

    [Fact]
    public void No_video_stream_means_no_media_info()
    {
        Assert.Null(FfprobeOutputParser.Parse("""{ "streams": [ { "codec_name": "mp3", "codec_type": "audio" } ], "format": {} }""", null));
        Assert.Null(FfprobeOutputParser.Parse("""{ "format": {} }""", null));
    }
}
