using System.Collections.Concurrent;
using Bigarr.Decision.CustomFormats;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Quality;

namespace Bigarr.Api.Infrastructure.Decision;

/// <summary>
/// The custom formats each file on disk matched, kept in memory between requests. Parsing a file name and running every
/// format's regexes costs about a tenth of a millisecond, which a library of thousands of files turns into most of a
/// Wanted page. Nothing is stored and nothing has to be told to forget: an answer is reused only for the same file with
/// exactly the inputs it was computed from (<see cref="FileFormatInput"/>) and the same custom formats
/// (<see cref="CustomFormatScorer.SignatureAsync"/>), so a renamed or replaced file, a refreshed year or an edited format
/// is matched again at its next read. Scores are not kept: they come from the version's profile at read time.
/// </summary>
public sealed class FileFormatMatches
{
    private readonly ConcurrentDictionary<int, Entry> _entries = new();

    /// <summary>Ids of the custom formats the file matches, computed when this file, these inputs and these formats were never seen together.</summary>
    public IReadOnlyList<int> Match(int fileId, FileFormatInput input, QualityDefinition? quality, CustomFormatCalculator calculator, string formatsSignature)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(calculator);

        if (_entries.TryGetValue(fileId, out var entry) && entry.FormatsSignature == formatsSignature && entry.Input.Equals(input))
        {
            return entry.FormatIds;
        }

        var matched = calculator.Match(CustomFormatScorer.FileInput(
            input.OriginalReleaseTitle, input.RelativePath, input.Size, input.IndexerFlags, quality, input.Languages, input.Edition, input.ReleaseGroup, input.Year, input.OriginalLanguage));
        var ids = matched.Select(m => m.Id).ToArray();
        _entries[fileId] = new Entry(input, formatsSignature, ids);
        return ids;
    }

    /// <summary>
    /// Forgets every file but these. Callers pass every file that still exists, not only the files they judged: the
    /// Wanted list and the library list share these entries and each judges files the other skips. So the cache never
    /// holds more than one entry per file in the library.
    /// </summary>
    public void Retain(IReadOnlySet<int> fileIds)
    {
        ArgumentNullException.ThrowIfNull(fileIds);
        foreach (var id in _entries.Keys)
        {
            if (!fileIds.Contains(id))
            {
                _entries.TryRemove(id, out _);
            }
        }
    }

    public int Count => _entries.Count;

    private sealed record Entry(FileFormatInput Input, string FormatsSignature, int[] FormatIds);
}

/// <summary>
/// What a file's custom formats are matched on (<see cref="CustomFormatScorer.FileInput"/>). The quality is its id: the
/// source, resolution and modifier of a definition are seeded and never edited.
/// </summary>
public sealed record FileFormatInput(
    string? OriginalReleaseTitle,
    string RelativePath,
    long Size,
    IndexerFlags IndexerFlags,
    int QualityId,
    IReadOnlyList<string> Languages,
    string? Edition,
    string? ReleaseGroup,
    int? Year,
    string? OriginalLanguage)
{
    public bool Equals(FileFormatInput? other) =>
        other is not null
        && OriginalReleaseTitle == other.OriginalReleaseTitle
        && RelativePath == other.RelativePath
        && Size == other.Size
        && IndexerFlags == other.IndexerFlags
        && QualityId == other.QualityId
        && Languages.SequenceEqual(other.Languages)
        && Edition == other.Edition
        && ReleaseGroup == other.ReleaseGroup
        && Year == other.Year
        && OriginalLanguage == other.OriginalLanguage;

    public override int GetHashCode() => HashCode.Combine(RelativePath, Size, QualityId, Year);
}
