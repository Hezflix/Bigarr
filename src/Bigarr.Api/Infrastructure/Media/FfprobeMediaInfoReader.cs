using System.Diagnostics;
using Bigarr.Domain.Movies;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Infrastructure.Media;

/// <summary>Runs ffprobe (part of ffmpeg) on a file. Missing binary: said once in the logs, media info stays empty.</summary>
public sealed class FfprobeMediaInfoReader(IOptions<MediaOptions> options, ILogger<FfprobeMediaInfoReader> logger) : IMediaInfoReader
{
    private readonly Lazy<string?> _executable = new(() => ExecutableLocator.Find(string.IsNullOrWhiteSpace(options.Value.FfprobePath) ? "ffprobe" : options.Value.FfprobePath));
    private int _warned;

    public bool IsAvailable => _executable.Value is not null;

    public string? ExecutablePath => _executable.Value;

    public async Task<MediaInfo?> ReadAsync(string path, string? releaseTitle, CancellationToken cancellationToken)
    {
        var probe = await ProbeAsync(path, cancellationToken);
        if (probe.Outcome != RawProbeOutcome.Read)
        {
            return null;
        }

        try
        {
            return FfprobeOutputParser.Parse(probe.Json!, releaseTitle);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ffprobe output on {Path} could not be read", path);
            return null;
        }
    }

    public async Task<RawProbe> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var executable = _executable.Value;
        if (executable is null)
        {
            if (Interlocked.Exchange(ref _warned, 1) == 0)
            {
                logger.LogWarning("ffprobe not found ({Configured}): files are imported without media info. Install ffmpeg or set Bigarr:Media:FfprobePath.", options.Value.FfprobePath);
            }

            return new RawProbe(RawProbeOutcome.ToolMissing, Error: "ffprobe was not found. Install ffmpeg or set Bigarr:Media:FfprobePath.");
        }

        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "-v", "error", "-print_format", "json", "-show_format", "-show_streams", "-i", path })
        {
            startInfo.ArgumentList.Add(argument);
        }

        var seconds = Math.Max(5, options.Value.ProbeTimeoutSeconds);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds));

        try
        {
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("ffprobe did not start.");
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                logger.LogWarning("ffprobe timed out on {Path}", path);
                return new RawProbe(RawProbeOutcome.TimedOut, Error: $"ffprobe gave no answer within {seconds} s.");
            }

            if (process.ExitCode != 0)
            {
                var error = (await stderr).Trim();
                logger.LogWarning("ffprobe failed on {Path} (exit {Code}): {Error}", path, process.ExitCode, error);
                return new RawProbe(RawProbeOutcome.Failed, Error: $"ffprobe exited with code {process.ExitCode}: {error}");
            }

            return RawProbe.Ok(await stdout);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "ffprobe could not read {Path}", path);
            return new RawProbe(RawProbeOutcome.Failed, Error: ex.Message);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // already gone
        }
    }
}
