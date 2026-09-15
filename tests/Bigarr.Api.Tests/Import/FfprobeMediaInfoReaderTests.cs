using System.Diagnostics;
using Bigarr.Api.Infrastructure.Media;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Tests.Import;

/// <summary>The real ffprobe on a real clip rendered by ffmpeg. Without the tools installed only the failure paths run; the parser has its own fixture tests.</summary>
public sealed class FfprobeMediaInfoReaderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("bigarr-ffprobe-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }

    [Fact]
    public async Task Probes_a_rendered_clip_when_the_tools_are_installed()
    {
        var reader = new FfprobeMediaInfoReader(Options.Create(new MediaOptions()), NullLogger<FfprobeMediaInfoReader>.Instance);
        var ffmpeg = ExecutableLocator.Find("ffmpeg");
        if (ffmpeg is null || !reader.IsAvailable)
        {
            return; // no ffmpeg here: nothing to render or probe
        }

        var clip = Path.Combine(_root, "Test.Clip.2026.1080p.WEB-DL.H.264-GRP.mkv");
        var render = Process.Start(new ProcessStartInfo(ffmpeg)
        {
            ArgumentList =
            {
                "-v", "error", "-y",
                "-f", "lavfi", "-i", "testsrc=duration=2:size=320x240:rate=10",
                "-f", "lavfi", "-i", "sine=frequency=440:duration=2",
                "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac",
                "-metadata:s:v:0", "language=eng", "-metadata:s:a:0", "language=fre",
                clip,
            },
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        })!;
        var stderr = await render.StandardError.ReadToEndAsync();
        await render.WaitForExitAsync();
        Assert.True(render.ExitCode == 0, stderr);

        var info = await reader.ReadAsync(clip, "Test.Clip.2026.1080p.WEB-DL.H.264-GRP", CancellationToken.None);

        Assert.NotNull(info);
        Assert.Equal("Matroska", info.ContainerFormat);
        Assert.Equal("h264", info.VideoCodec); // the release says H.264
        Assert.Equal(320, info.Width);
        Assert.Equal(240, info.Height);
        Assert.Equal(8, info.VideoBitDepth);
        Assert.Equal("Progressive", info.ScanType);
        Assert.Null(info.VideoDynamicRange);
        Assert.Equal("AAC", info.AudioCodec);
        Assert.Equal(1.0, info.AudioChannels);
        Assert.Equal(["French"], info.AudioLanguages);
        Assert.Empty(info.Subtitles);
        Assert.NotNull(info.RunTime);
        Assert.InRange(info.RunTime.Value.TotalSeconds, 1.5, 2.5);
    }

    [Fact]
    public async Task A_missing_binary_or_an_unreadable_file_yields_null()
    {
        var garbage = Path.Combine(_root, "garbage.mkv");
        await File.WriteAllBytesAsync(garbage, new byte[4096]);

        var missing = new FfprobeMediaInfoReader(Options.Create(new MediaOptions { FfprobePath = Path.Combine(_root, "nope", "ffprobe") }), NullLogger<FfprobeMediaInfoReader>.Instance);
        Assert.False(missing.IsAvailable);
        Assert.Null(await missing.ReadAsync(garbage, null, CancellationToken.None));

        var real = new FfprobeMediaInfoReader(Options.Create(new MediaOptions()), NullLogger<FfprobeMediaInfoReader>.Instance);
        if (real.IsAvailable)
        {
            Assert.Null(await real.ReadAsync(garbage, null, CancellationToken.None));
        }
    }
}
