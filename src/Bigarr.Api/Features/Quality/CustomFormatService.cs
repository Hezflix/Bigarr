using System.Text.Json;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Decision;
using Bigarr.Decision.CustomFormats;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Quality;
using Bigarr.Parsing;
using Bigarr.Parsing.Languages;
using Bigarr.Parsing.Model;
using Microsoft.EntityFrameworkCore;
using ParserModifier = Bigarr.Parsing.Qualities.Modifier;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;
using ParserSource = Bigarr.Parsing.Qualities.QualitySource;

namespace Bigarr.Api.Features.Quality;

/// <summary>
/// Custom formats (§3.6): stored in the Radarr JSON shape so exports and guide files import unchanged, evaluated at read
/// time so editing one needs no recalculation. Scores live on the profile × format join table and can be set from
/// either side.
/// </summary>
public sealed class CustomFormatService(BigarrDbContext db, CustomFormatScorer scorer, ILogger<CustomFormatService> logger)
{
    public const int MaxNameLength = 128;

    /// <summary>Radarr's implementation names with the fields the editor needs to build each condition by hand.</summary>
    /// <summary>Sonarr's release types (episodes only; a film never matches).</summary>
    private static readonly List<SpecificationOptionDto> ReleaseTypes =
    [
        new(1, "Single episode"),
        new(2, "Multi-episode"),
        new(3, "Season pack"),
        new(4, "Multi-season pack"),
    ];

    public static CustomFormatSchemaResponse Schema()
    {
        List<SpecificationFieldSchemaDto> regex = [new("value", "regex", true)];
        List<SpecificationFieldSchemaDto> range = [new("min", "number", false), new("max", "number", true)];

        var sources = Enum.GetValues<ParserSource>().Select(s => new SpecificationOptionDto((int)s, SourceLabel(s))).ToList();
        var resolutions = new[] { 2160, 1080, 720, 576, 540, 480, 360 }.Select(r => new SpecificationOptionDto(r, $"{r}p")).ToList();
        var modifiers = Enum.GetValues<ParserModifier>().Select(m => new SpecificationOptionDto((int)m, ModifierLabel(m))).ToList();
        var languages = new[] { Language.Original }.Concat(Language.All.Where(l => l.Id > 0)).Select(l => new SpecificationOptionDto(l.Id, l.Name)).ToList();
        var flags = Enum.GetValues<IndexerFlags>().Where(f => f != IndexerFlags.None).Select(f => new SpecificationOptionDto((int)f, FlagLabel(f))).ToList();

        return new CustomFormatSchemaResponse(
        [
            new("ReleaseTitleSpecification", regex),
            new("ReleaseGroupSpecification", regex),
            new("EditionSpecification", regex),
            new("SourceSpecification", [new("value", "select", true, sources)]),
            new("ResolutionSpecification", [new("value", "select", true, resolutions)]),
            new("QualityModifierSpecification", [new("value", "select", true, modifiers)]),
            new("LanguageSpecification", [new("value", "select", true, languages), new("exceptLanguage", "boolean", false)]),
            new("IndexerFlagSpecification", [new("value", "select", true, flags)]),
            new("SizeSpecification", range),
            new("YearSpecification", range),
            new("ReleaseTypeSpecification", [new("value", "select", true, ReleaseTypes)]),
        ]);
    }

    public async Task<List<CustomFormatResponse>> ListAsync(CancellationToken ct)
    {
        var formats = await Query().OrderBy(f => f.Name).ToListAsync(ct);
        return formats.Select(ToResponse).ToList();
    }

    public async Task<CustomFormatResponse?> GetAsync(int id, CancellationToken ct)
    {
        var format = await Query().FirstOrDefaultAsync(f => f.Id == id, ct);
        return format is null ? null : ToResponse(format);
    }

    public async Task<Result<CustomFormatResponse>> AddAsync(CustomFormatRequest request, CancellationToken ct)
    {
        var (specifications, invalid) = await ValidateAsync(request, null, ct);
        if (invalid is not null)
        {
            return invalid;
        }

        var format = new CustomFormat { Name = request.Name.Trim(), SpecificationsJson = CustomFormatJson.SerializeSpecifications(specifications!), IncludeWhenRenaming = request.IncludeWhenRenaming };
        ApplyScores(format, request.Scores);
        db.CustomFormats.Add(format);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Custom format {Name} added", format.Name);
        db.ChangeTracker.Clear();
        return Result<CustomFormatResponse>.Ok(ToResponse(await Query().FirstAsync(f => f.Id == format.Id, ct)));
    }

    public async Task<Result<CustomFormatResponse>> UpdateAsync(int id, CustomFormatRequest request, CancellationToken ct)
    {
        var format = await db.CustomFormats.Include(f => f.Scores).FirstOrDefaultAsync(f => f.Id == id, ct);
        if (format is null)
        {
            return Failure.NotFound("Custom format");
        }

        var (specifications, invalid) = await ValidateAsync(request, id, ct);
        if (invalid is not null)
        {
            return invalid;
        }

        format.Name = request.Name.Trim();
        format.IncludeWhenRenaming = request.IncludeWhenRenaming;
        format.SpecificationsJson = CustomFormatJson.SerializeSpecifications(specifications!);
        ApplyScores(format, request.Scores);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return Result<CustomFormatResponse>.Ok(ToResponse(await Query().FirstAsync(f => f.Id == id, ct)));
    }

    public async Task<Result<bool>> DeleteAsync(int id, CancellationToken ct)
    {
        var format = await db.CustomFormats.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (format is null)
        {
            return Failure.NotFound("Custom format");
        }

        // Scores cascade; version constraints referencing the format are cleaned so no version waits for a format that no longer exists.
        var constrained = await db.MovieVersions.Where(v => v.Constraints != null).ToListAsync(ct);
        foreach (var version in constrained)
        {
            var constraints = version.Constraints!;
            if (constraints.RequiredCustomFormatIds.Remove(id) | constraints.ForbiddenCustomFormatIds.Remove(id))
            {
                version.Constraints = constraints.IsEmpty ? null : new AcquisitionConstraints
                {
                    Edition = constraints.Edition,
                    ReleaseGroup = constraints.ReleaseGroup,
                    RequiredCustomFormatIds = constraints.RequiredCustomFormatIds,
                    ForbiddenCustomFormatIds = constraints.ForbiddenCustomFormatIds,
                };
            }
        }

        db.CustomFormats.Remove(format);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Custom format {Name} deleted", format.Name);
        return Result<bool>.Ok(true);
    }

    /// <summary>
    /// Imports Radarr export or guide JSON, one format or an array, as-is. A format already present (same guide id, else
    /// same name) is updated. Scores are untouched unless profiles are given, in which case the guide's score for
    /// <paramref name="scoreKey"/> is applied to them.
    /// </summary>
    public async Task<Result<CustomFormatImportResponse>> ImportAsync(string json, IReadOnlyList<int> profileIds, string scoreKey, CancellationToken ct)
    {
        IReadOnlyList<CustomFormatDefinition> definitions;
        try
        {
            definitions = CustomFormatJson.ParseMany(json);
        }
        catch (CustomFormatParseException ex)
        {
            return Failure.Invalid("Custom format JSON rejected", ex.Message);
        }

        if (definitions.Count == 0)
        {
            return Failure.Invalid("Nothing to import", "The JSON holds no custom format.");
        }

        var profiles = profileIds.Count == 0
            ? []
            : await db.QualityProfiles.Include(p => p.FormatScores).Where(p => profileIds.Contains(p.Id)).ToListAsync(ct);
        var unknownProfiles = profileIds.Except(profiles.Select(p => p.Id)).ToList();
        if (unknownProfiles.Count > 0)
        {
            return Failure.Invalid("Unknown quality profile", $"No profile with id {string.Join(", ", unknownProfiles)}.");
        }

        var existing = await db.CustomFormats.ToListAsync(ct);
        var added = new List<CustomFormat>();
        var updated = new List<CustomFormat>();
        var applied = new List<string>();

        foreach (var definition in definitions)
        {
            var format = (definition.TrashId is not null ? existing.FirstOrDefault(f => f.TrashId == definition.TrashId) : null)
                         ?? existing.FirstOrDefault(f => string.Equals(f.Name, definition.Name, StringComparison.OrdinalIgnoreCase));

            if (format is null)
            {
                format = new CustomFormat { Name = definition.Name, SpecificationsJson = string.Empty };
                db.CustomFormats.Add(format);
                existing.Add(format);
                added.Add(format);
            }
            else
            {
                updated.Add(format);
            }

            format.Name = definition.Name;
            format.IncludeWhenRenaming = definition.IncludeWhenRenaming;
            format.SpecificationsJson = CustomFormatJson.SerializeSpecifications(definition.Specifications);
            format.TrashId = definition.TrashId ?? format.TrashId;
            format.TrashScoresJson = definition.TrashScores is null ? format.TrashScoresJson : CustomFormatJson.SerializeScores(definition.TrashScores);

            if (profiles.Count == 0)
            {
                continue;
            }

            if (definition.TrashScores is null || !definition.TrashScores.TryGetValue(scoreKey, out var score))
            {
                applied.Add($"{definition.Name}: no '{scoreKey}' score in the guide, profiles left unchanged");
                continue;
            }

            foreach (var profile in profiles)
            {
                var entry = profile.FormatScores.FirstOrDefault(s => s.CustomFormat == format || (format.Id != 0 && s.CustomFormatId == format.Id));
                if (entry is null)
                {
                    profile.FormatScores.Add(new ProfileFormatScore { CustomFormat = format, Score = score });
                }
                else
                {
                    entry.Score = score;
                }

                applied.Add($"{definition.Name} → {profile.Name}: {score}");
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Imported {Added} custom format(s), updated {Updated}, {Scores} score(s) applied", added.Count, updated.Count, applied.Count);

        var ids = added.Concat(updated).Select(f => f.Id).ToList();
        db.ChangeTracker.Clear();
        var reloaded = await Query().Where(f => ids.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);
        return Result<CustomFormatImportResponse>.Ok(new CustomFormatImportResponse(
            added.Select(f => ToResponse(reloaded[f.Id])).ToList(),
            updated.Select(f => ToResponse(reloaded[f.Id])).ToList(),
            applied));
    }

    /// <summary>What the saved formats make of a release title, and the score each profile would give. The acceptance test of the module.</summary>
    public async Task<Result<CustomFormatEvaluationResponse>> EvaluateAsync(string title, long? size, IndexerFlags flags, int? year, string? originalLanguageIso, CancellationToken ct)
    {
        var parsed = TryParse(title);
        if (parsed is null)
        {
            return Failure.Invalid("Title could not be parsed", $"'{title}' does not look like a release title.");
        }

        var input = CustomFormatInput.FromParsed(parsed, null, size ?? 0, flags, year, CustomFormatInput.LanguageFromIsoCode(originalLanguageIso));
        var matched = await scorer.MatchAsync(input, ct);
        var ids = matched.Select(m => m.Id).ToList();
        var profiles = await db.QualityProfiles.AsNoTracking().Include(p => p.FormatScores).OrderBy(p => p.Name).ToListAsync(ct);
        var quality = parsed.Quality?.Quality ?? ParserQuality.Unknown;

        return Result<CustomFormatEvaluationResponse>.Ok(new CustomFormatEvaluationResponse(
            title,
            parsed.PrimaryMovieTitle,
            parsed.Year > 0 ? parsed.Year : null,
            quality.Name,
            string.IsNullOrWhiteSpace(parsed.Edition) ? null : parsed.Edition,
            string.IsNullOrWhiteSpace(parsed.ReleaseGroup) ? null : parsed.ReleaseGroup,
            input.Languages.Select(l => l.Name).ToList(),
            matched.Select(m => new MatchedFormatDto(m.Id, m.Name)).ToList(),
            profiles.Select(p => new CustomFormatScoreDto(p.Id, p.Name, ProfileRanking.FormatScore(p, ids))).ToList()));
    }

    /// <summary>Judges a draft that is not saved yet, condition by condition: the editor's test button.</summary>
    public Result<CustomFormatTestResponse> Test(CustomFormatTestRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Failure.Invalid("Title is required");
        }

        var parsed = TryParse(request.Title.Trim());
        if (parsed is null)
        {
            return Failure.Invalid("Title could not be parsed", $"'{request.Title}' does not look like a release title.");
        }

        var (definitions, invalid) = BuildSpecifications(request.Specifications, "draft");
        if (invalid is not null)
        {
            return invalid;
        }

        var specifications = definitions!.Select(d => CustomFormatSpecificationFactory.Create(d, "draft")).ToList();
        var input = CustomFormatInput.FromParsed(parsed, null, request.Size ?? 0, IndexerFlags.None, null, CustomFormatInput.LanguageFromIsoCode(request.OriginalLanguage));
        var quality = parsed.Quality?.Quality ?? ParserQuality.Unknown;

        return Result<CustomFormatTestResponse>.Ok(new CustomFormatTestResponse(
            CustomFormatCalculator.Matches(specifications, input),
            parsed.PrimaryMovieTitle,
            parsed.Year > 0 ? parsed.Year : null,
            quality.Name,
            string.IsNullOrWhiteSpace(parsed.Edition) ? null : parsed.Edition,
            string.IsNullOrWhiteSpace(parsed.ReleaseGroup) ? null : parsed.ReleaseGroup,
            input.Languages.Select(l => l.Name).ToList(),
            specifications.Select(s => new SpecificationVerdictDto(s.Name, s.Implementation, s.IsSatisfiedBy(input))).ToList()));
    }

    private static ParsedMovieInfo? TryParse(string title)
    {
        try
        {
            return Parser.ParseMovieTitle(title);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private IQueryable<CustomFormat> Query() =>
        db.CustomFormats.AsNoTracking().Include(f => f.Scores).ThenInclude(s => s.QualityProfile);

    /// <summary>Null leaves the scores alone; a list replaces them, zero dropping the row.</summary>
    private static void ApplyScores(CustomFormat format, List<CustomFormatScoreRequest>? scores)
    {
        if (scores is null)
        {
            return;
        }

        var wanted = scores.Where(s => s.Score != 0).ToDictionary(s => s.QualityProfileId, s => s.Score);
        foreach (var score in format.Scores.ToList())
        {
            if (wanted.TryGetValue(score.QualityProfileId, out var value))
            {
                score.Score = value;
                wanted.Remove(score.QualityProfileId);
            }
            else
            {
                format.Scores.Remove(score);
            }
        }

        foreach (var (qualityProfileId, value) in wanted)
        {
            format.Scores.Add(new ProfileFormatScore { QualityProfileId = qualityProfileId, Score = value });
        }
    }

    private async Task<(IReadOnlyList<CustomFormatSpecificationDefinition>? Specifications, Failure? Failure)> ValidateAsync(CustomFormatRequest request, int? existingId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return (null, Failure.Invalid("Name is required"));
        }

        var name = request.Name.Trim();
        if (name.Length > MaxNameLength)
        {
            return (null, Failure.Invalid("Name is too long", $"At most {MaxNameLength} characters."));
        }

        if (await db.CustomFormats.AnyAsync(f => f.Id != existingId && f.Name.ToLower() == name.ToLower(), ct))
        {
            return (null, Failure.Conflict("Custom format name already used", $"A custom format named '{name}' exists."));
        }

        if (request.Specifications is null || request.Specifications.Count == 0)
        {
            return (null, Failure.Invalid("Specifications are required", "A custom format without specification would match everything."));
        }

        if (request.Scores is { Count: > 0 })
        {
            var ids = request.Scores.Select(s => s.QualityProfileId).ToList();
            if (ids.Distinct().Count() != ids.Count)
            {
                return (null, Failure.Invalid("Duplicated profile score"));
            }

            var known = await db.QualityProfiles.Where(p => ids.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct);
            var unknown = ids.Except(known).ToList();
            if (unknown.Count > 0)
            {
                return (null, Failure.Invalid("Unknown quality profile", $"No profile with id {string.Join(", ", unknown)}."));
            }
        }

        return BuildSpecifications(request.Specifications, name);
    }

    private static (IReadOnlyList<CustomFormatSpecificationDefinition>? Specifications, Failure? Failure) BuildSpecifications(List<CustomFormatSpecificationDto>? dtos, string formatName)
    {
        if (dtos is null || dtos.Count == 0)
        {
            return (null, Failure.Invalid("Specifications are required", "A custom format without specification would match everything."));
        }

        var specifications = new List<CustomFormatSpecificationDefinition>();
        foreach (var dto in dtos)
        {
            var definition = new CustomFormatSpecificationDefinition(
                (string.IsNullOrWhiteSpace(dto.Name) ? dto.Implementation : dto.Name.Trim()) ?? string.Empty,
                dto.Implementation?.Trim() ?? string.Empty,
                dto.Negate,
                dto.Required,
                new Dictionary<string, JsonElement>(dto.Fields ?? [], StringComparer.OrdinalIgnoreCase));
            try
            {
                CustomFormatSpecificationFactory.Create(definition, formatName);
            }
            catch (CustomFormatParseException ex)
            {
                return (null, Failure.Invalid("Invalid specification", ex.Message));
            }

            specifications.Add(definition);
        }

        return (specifications, null);
    }

    private static string SourceLabel(ParserSource source) => source switch
    {
        ParserSource.UNKNOWN => "Unknown",
        ParserSource.CAM => "CAM",
        ParserSource.TELESYNC => "Telesync",
        ParserSource.TELECINE => "Telecine",
        ParserSource.WORKPRINT => "Workprint",
        ParserSource.DVD => "DVD",
        ParserSource.TV => "TV",
        ParserSource.WEBDL => "WEB-DL",
        ParserSource.WEBRIP => "WEBRip",
        ParserSource.BLURAY => "Blu-ray",
        _ => source.ToString(),
    };

    private static string ModifierLabel(ParserModifier modifier) => modifier switch
    {
        ParserModifier.NONE => "None",
        ParserModifier.REGIONAL => "Regional",
        ParserModifier.SCREENER => "Screener",
        ParserModifier.RAWHD => "Raw HD",
        ParserModifier.BRDISK => "BR-DISK",
        ParserModifier.REMUX => "Remux",
        _ => modifier.ToString(),
    };

    private static string FlagLabel(IndexerFlags flag) => flag switch
    {
        IndexerFlags.DoubleUpload => "Double upload",
        IndexerFlags.Freeleech75 => "Freeleech 75%",
        IndexerFlags.Freeleech25 => "Freeleech 25%",
        _ => flag.ToString(),
    };

    public static CustomFormatResponse ToResponse(CustomFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);

        List<CustomFormatSpecificationDto> specifications;
        try
        {
            specifications = CustomFormatJson.ParseSpecifications(format.SpecificationsJson, format.Name)
                .Select(s => new CustomFormatSpecificationDto(s.Name, s.Implementation, s.Negate, s.Required, new Dictionary<string, JsonElement>(s.Fields)))
                .ToList();
        }
        catch (CustomFormatParseException)
        {
            specifications = [];
        }

        var trashScores = CustomFormatJson.ParseScores(format.TrashScoresJson);
        return new CustomFormatResponse(
            format.Id,
            format.Name,
            format.IncludeWhenRenaming,
            specifications,
            format.TrashId,
            trashScores is null ? null : new Dictionary<string, int>(trashScores),
            format.Scores.OrderBy(s => s.QualityProfile?.Name).Select(s => new CustomFormatScoreDto(s.QualityProfileId, s.QualityProfile?.Name ?? $"#{s.QualityProfileId}", s.Score)).ToList(),
            format.Note);
    }
}
