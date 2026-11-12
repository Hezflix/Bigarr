using System.Globalization;
using Bigarr.Api.Infrastructure.RadarrImport;
using Bigarr.Domain.History;

namespace Bigarr.Api.Features.Migration;

/// <summary>What one *arr history line carries, the same in Radarr's and Sonarr's API.</summary>
public interface IArrHistoryItem
{
    int Id { get; }

    string? SourceTitle { get; }

    RadarrQuality? Quality { get; }

    List<RadarrCustomFormatRef>? CustomFormats { get; }

    DateTime Date { get; }

    string? DownloadId { get; }

    string? EventType { get; }

    Dictionary<string, string?>? Data { get; }
}

/// <summary>An *arr history line translated: its Bigarr type, UTC date and the data keys the history page reads.</summary>
public sealed record ArrHistoryLine<TRecord>(TRecord Record, HistoryEventType Type, DateTime Date, Dictionary<string, string> Data);

/// <summary>Where Radarr's and Sonarr's histories differ: their name, event names and quality names.</summary>
/// <param name="Source">Written as <c>importedFrom</c>: "radarr", "sonarr".</param>
/// <param name="IdKey">The data key holding the source's line id, so a line is never copied twice.</param>
/// <param name="TypeOf">The source's event type in Bigarr's terms; null for events Bigarr has no counterpart for.</param>
/// <param name="QualityName">The Bigarr name of the source's quality.</param>
/// <param name="KeepsReleaseType">Sonarr says whether a grab was a single episode or a season pack.</param>
public sealed record ArrHistoryDialect(
    string Source,
    string IdKey,
    Func<string?, HistoryEventType?> TypeOf,
    Func<RadarrQuality?, string?> QualityName,
    bool KeepsReleaseType = false);

public static class ArrHistory
{
    /// <summary>
    /// The lines of one film or episode, oldest first, in Bigarr's terms. The *arrs have no upgrade event: an import that
    /// follows an earlier import, with no deletion in between other than the upgrade's own, becomes
    /// <see cref="HistoryEventType.Upgraded"/>. Events Bigarr has no counterpart for (<c>downloadIgnored</c>,
    /// <c>unknown</c>) are left out. A partial history (the follow's lines since its checkpoint) starts from the file the
    /// item had: <paramref name="hadFile"/>.
    /// </summary>
    public static IEnumerable<ArrHistoryLine<TRecord>> Lines<TRecord>(IEnumerable<TRecord> records, ArrHistoryDialect dialect, bool hadFile = false, string? fileQuality = null)
        where TRecord : IArrHistoryItem
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(dialect);

        var currentQuality = fileQuality;
        var hasFile = hadFile;
        foreach (var record in records)
        {
            if (dialect.TypeOf(record.EventType) is not { } type)
            {
                continue;
            }

            var raw = new Dictionary<string, string?>(record.Data ?? [], StringComparer.OrdinalIgnoreCase);
            string? Raw(string key) => raw.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

            var data = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["importedFrom"] = dialect.Source,
                [dialect.IdKey] = record.Id.ToString(CultureInfo.InvariantCulture),
            };
            void Put(string key, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    data[key] = value.Trim();
                }
            }

            var quality = dialect.QualityName(record.Quality);
            Put("quality", quality is "Unknown" ? null : quality);
            Put("indexer", Raw("indexer"));
            switch (type)
            {
                case HistoryEventType.Grabbed:
                    Put("downloadClient", Raw("downloadClientName") ?? Raw("downloadClient"));
                    Put("downloadId", record.DownloadId);
                    Put("size", Raw("size"));
                    Put("releaseGuid", Raw("guid"));
                    Put("releaseGroup", Raw("releaseGroup"));
                    Put("protocol", Raw("protocol"));
                    if (dialect.KeepsReleaseType)
                    {
                        Put("releaseType", Raw("releaseType"));
                    }

                    Put("indexerFlags", Raw("indexerFlags") is { } flags && flags != "0" ? flags : null);
                    break;
                case HistoryEventType.Imported:
                    Put("path", Raw("importedPath"));
                    Put("source", Raw("droppedPath"));
                    Put("size", Raw("size"));
                    Put("downloadClient", Raw("downloadClientName") ?? Raw("downloadClient"));
                    Put("downloadId", record.DownloadId);
                    if (hasFile)
                    {
                        type = HistoryEventType.Upgraded;
                        Put("previousQuality", currentQuality);
                    }

                    hasFile = true;
                    currentQuality = data.GetValueOrDefault("quality");
                    break;
                case HistoryEventType.DownloadFailed:
                    Put("reason", Raw("message"));
                    Put("downloadClient", Raw("downloadClientName") ?? Raw("downloadClient"));
                    Put("downloadId", record.DownloadId);
                    break;
                case HistoryEventType.FileDeleted:
                    Put("reason", Raw("reason"));
                    Put("path", record.SourceTitle);
                    Put("size", Raw("size"));
                    if (!string.Equals(Raw("reason"), "upgrade", StringComparison.OrdinalIgnoreCase))
                    {
                        hasFile = false;
                        currentQuality = null;
                    }

                    break;
                case HistoryEventType.Renamed:
                    Put("from", Raw("sourcePath"));
                    Put("to", Raw("path"));
                    break;
            }

            if (record.CustomFormats is { Count: > 0 } formats)
            {
                Put("customFormats", string.Join(", ", formats.Select(f => f.Name).Where(n => !string.IsNullOrWhiteSpace(n))));
            }

            yield return new ArrHistoryLine<TRecord>(record, type, Utc(record.Date), data);
        }
    }

    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
