using System.Text.Json;

namespace Bigarr.Api.Infrastructure.Media;

/// <summary>ffprobe's whole output on a library file, read now: <see cref="Probe"/> is its JSON (streams and format) as it came.</summary>
public sealed record MediaProbeResponse(string Path, DateTime ProbedAt, JsonElement Probe);

/// <summary>Runs the probe on demand on a file of the library, for the file details of the film and episode pages. Nothing is stored.</summary>
public sealed class MediaProbeService(IMediaInfoReader reader, TimeProvider time)
{
    public async Task<Result<MediaProbeResponse>> ProbeAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return new Failure(StatusCodes.Status404NotFound, "File not on disk", $"{path} does not exist.");
        }

        var probe = await reader.ProbeAsync(path, ct);
        switch (probe.Outcome)
        {
            case RawProbeOutcome.ToolMissing:
                return Failure.Conflict("ffprobe not available", probe.Error);
            case RawProbeOutcome.TimedOut:
            case RawProbeOutcome.Failed:
                return Failure.Conflict("ffprobe could not read the file", probe.Error);
        }

        JsonElement json;
        try
        {
            using var document = JsonDocument.Parse(probe.Json!);
            json = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return Failure.Conflict("ffprobe could not read the file", $"Its output is not JSON: {ex.Message}");
        }

        return Result<MediaProbeResponse>.Ok(new MediaProbeResponse(path, time.GetUtcNow().UtcDateTime, json));
    }
}
