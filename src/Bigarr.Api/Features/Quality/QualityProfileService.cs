using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Quality;
using Bigarr.Parsing;
using Microsoft.EntityFrameworkCore;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Features.Quality;

/// <summary>Quality profiles and definitions. Ranks exist only inside a profile: the items list must hold every quality once.</summary>
public sealed class QualityProfileService(BigarrDbContext db, ILogger<QualityProfileService> logger)
{
    public const int MaxNameLength = 128;

    public async Task<List<QualityProfileSummary>> ListAsync(CancellationToken ct) =>
        await db.QualityProfiles.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new QualityProfileSummary(p.Id, p.Name, p.Cutoff, p.UpgradeAllowed, p.IsSystem, p.PreferredLanguage, p.Kind))
            .ToListAsync(ct);

    public async Task<QualityProfileResponse?> GetAsync(int id, CancellationToken ct)
    {
        var profile = await Query().FirstOrDefaultAsync(p => p.Id == id, ct);
        return profile is null ? null : await ToResponseAsync(profile, ct);
    }

    /// <summary>A template for a new profile: every quality in Radarr's order, nothing allowed yet.</summary>
    public async Task<QualityProfileResponse> SchemaAsync(CancellationToken ct)
    {
        var definitions = ParserQuality.DefaultQualityDefinitions.OrderBy(d => d.Weight).ThenBy(d => d.Quality.Id).ToList();
        var template = QualitySeeder.BuildHdProfile(definitions);
        foreach (var item in template.Items)
        {
            item.Allowed = false;
        }

        template.Name = string.Empty;
        template.Cutoff = 0;
        template.IsSystem = false;
        return await ToResponseAsync(template, ct);
    }

    public async Task<Result<QualityProfileResponse>> AddAsync(QualityProfileRequest request, CancellationToken ct)
    {
        var invalid = await ValidateAsync(request, null, ct);
        if (invalid is not null)
        {
            return invalid;
        }

        var profile = new QualityProfile { Name = request.Name.Trim(), PreferredLanguage = request.PreferredLanguage.Trim(), Kind = request.Kind };
        Apply(profile, request);
        db.QualityProfiles.Add(profile);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Quality profile {Name} added", profile.Name);
        return Result<QualityProfileResponse>.Ok(await ToResponseAsync(await Query().FirstAsync(p => p.Id == profile.Id, ct), ct));
    }

    public async Task<Result<QualityProfileResponse>> UpdateAsync(int id, QualityProfileRequest request, CancellationToken ct)
    {
        var profile = await db.QualityProfiles.Include(p => p.FormatScores).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (profile is null)
        {
            return Failure.NotFound("Quality profile");
        }

        var invalid = await ValidateAsync(request, id, ct);
        if (invalid is not null)
        {
            return invalid;
        }

        if (request.Kind != profile.Kind)
        {
            // A profile in use cannot leave its versions with a profile of the wrong kind.
            var films = request.Kind.ForMovies() ? 0 : await db.MovieVersions.CountAsync(v => v.QualityProfileId == id, ct);
            var shows = request.Kind.ForSeries() ? 0 : await db.SeriesVersions.CountAsync(v => v.QualityProfileId == id, ct);
            if (films > 0 || shows > 0)
            {
                return Failure.Invalid(
                    "Kind not possible while in use",
                    films > 0
                        ? $"{films} film version(s) use '{profile.Name}': it must stay Films or Both until they move to another profile."
                        : $"{shows} series version(s) use '{profile.Name}': it must stay Series or Both until they move to another profile.");
            }

            profile.Kind = request.Kind;
        }

        profile.Name = request.Name.Trim();
        profile.PreferredLanguage = request.PreferredLanguage.Trim();
        Apply(profile, request);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return Result<QualityProfileResponse>.Ok(await ToResponseAsync(await Query().FirstAsync(p => p.Id == id, ct), ct));
    }

    public async Task<Result<bool>> DeleteAsync(int id, CancellationToken ct)
    {
        var profile = await db.QualityProfiles.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (profile is null)
        {
            return Failure.NotFound("Quality profile");
        }

        if (profile.IsSystem)
        {
            return Failure.Conflict("System profile", $"'{profile.Name}' is seeded by Bigarr and cannot be deleted. Edit it instead.");
        }

        var inUse = await db.MovieVersions.CountAsync(v => v.QualityProfileId == id, ct) + await db.SeriesVersions.CountAsync(v => v.QualityProfileId == id, ct);
        if (inUse > 0)
        {
            return Failure.Conflict("Quality profile in use", $"{inUse} version(s) use '{profile.Name}'. Move them to another profile first.");
        }

        db.QualityProfiles.Remove(profile);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Quality profile {Name} deleted", profile.Name);
        return Result<bool>.Ok(true);
    }

    public async Task<List<QualityDefinitionResponse>> ListDefinitionsAsync(CancellationToken ct) =>
        (await db.QualityDefinitions.AsNoTracking().ToListAsync(ct))
        .OrderBy(q => q.Resolution).ThenBy(q => q.Source)
        .Select(ToResponse)
        .ToList();

    public async Task<Result<QualityDefinitionResponse>> UpdateDefinitionAsync(int id, QualityDefinitionUpdateRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var definition = await db.QualityDefinitions.FirstOrDefaultAsync(q => q.Id == id, ct);
        if (definition is null)
        {
            return Failure.NotFound("Quality definition");
        }

        if (request.Title is { Length: > 64 })
        {
            return Failure.Invalid("Title is too long", "At most 64 characters.");
        }

        if (request.MinSize is < 0 || request.PreferredSize is < 0 || request.MaxSize is < 0)
        {
            return Failure.Invalid("Invalid size", "Sizes are MB per minute, zero or more.");
        }

        if (request.MaxSize is { } max && request.MinSize is { } min && min > max)
        {
            return Failure.Invalid("Invalid size", "The minimum size cannot exceed the maximum.");
        }

        if (request.PreferredSize is { } preferred && ((request.MinSize is { } lo && preferred < lo) || (request.MaxSize is { } hi && preferred > hi)))
        {
            return Failure.Invalid("Invalid size", "The preferred size must lie between the minimum and the maximum.");
        }

        definition.Title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim();
        if (request.Kind == Bigarr.Domain.Library.MediaKind.Series)
        {
            definition.SeriesMinSize = request.MinSize;
            definition.SeriesPreferredSize = request.PreferredSize;
            definition.SeriesMaxSize = request.MaxSize;
        }
        else
        {
            definition.MinSize = request.MinSize;
            definition.PreferredSize = request.PreferredSize;
            definition.MaxSize = request.MaxSize;
        }
        await db.SaveChangesAsync(ct);
        return Result<QualityDefinitionResponse>.Ok(ToResponse(definition));
    }

    private IQueryable<QualityProfile> Query() =>
        db.QualityProfiles.AsNoTracking().Include(p => p.FormatScores).ThenInclude(s => s.CustomFormat);

    private async Task<Failure?> ValidateAsync(QualityProfileRequest request, int? existingId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Failure.Invalid("Name is required");
        }

        if (request.Name.Trim().Length > MaxNameLength)
        {
            return Failure.Invalid("Name is too long", $"At most {MaxNameLength} characters.");
        }

        var name = request.Name.Trim();
        if (await db.QualityProfiles.AnyAsync(p => p.Id != existingId && p.Name.ToLower() == name.ToLower(), ct))
        {
            return Failure.Conflict("Quality profile name already used", $"A profile named '{name}' exists.");
        }

        if (string.IsNullOrWhiteSpace(request.PreferredLanguage) || IsoLanguages.Find(request.PreferredLanguage.Trim()) is null)
        {
            return Failure.Invalid("Invalid preferred language", "Use an ISO 639-1 code such as en or fr.");
        }

        if (request.MinUpgradeFormatScore < 0)
        {
            return Failure.Invalid("Invalid minimum upgrade score", "Zero or more.");
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return Failure.Invalid("Items are required", "Fetch /api/v1/qualityprofiles/schema for the full ordered list.");
        }

        var known = (await db.QualityDefinitions.AsNoTracking().Select(q => q.Id).ToListAsync(ct)).ToHashSet();
        var seen = new HashSet<int>();
        var groupIds = new HashSet<int>();
        foreach (var item in request.Items)
        {
            if (item.QualityIds is null || item.QualityIds.Count == 0)
            {
                return Failure.Invalid("Invalid item", "Each item needs at least one quality.");
            }

            foreach (var qualityId in item.QualityIds)
            {
                if (!known.Contains(qualityId))
                {
                    return Failure.Invalid("Unknown quality", $"No quality definition with id {qualityId}.");
                }

                if (!seen.Add(qualityId))
                {
                    return Failure.Invalid("Duplicated quality", $"Quality {qualityId} appears twice.");
                }
            }

            if (item.QualityIds.Count > 1 || item.GroupId is not null)
            {
                if (item.GroupId is null or < QualitySeeder.FirstGroupId)
                {
                    return Failure.Invalid("Invalid group", $"A group needs an id of {QualitySeeder.FirstGroupId} or more.");
                }

                if (!groupIds.Add(item.GroupId.Value))
                {
                    return Failure.Invalid("Duplicated group", $"Group {item.GroupId} appears twice.");
                }

                if (string.IsNullOrWhiteSpace(item.GroupName))
                {
                    return Failure.Invalid("Invalid group", "A group needs a name.");
                }
            }
        }

        var missing = known.Except(seen).ToList();
        if (missing.Count > 0)
        {
            return Failure.Invalid("Incomplete items", $"Every quality must appear once so ranks are total; missing: {string.Join(", ", missing.OrderBy(m => m))}.");
        }

        if (!request.Items.Any(i => i.Allowed))
        {
            return Failure.Invalid("Nothing allowed", "Allow at least one quality.");
        }

        var cutoffItem = request.Items.FirstOrDefault(i => i.Allowed && (i.GroupId is not null ? i.GroupId == request.Cutoff : i.QualityIds[0] == request.Cutoff));
        if (cutoffItem is null)
        {
            return Failure.Invalid("Invalid cutoff", "The cutoff must be the quality id (or group id) of an allowed item.");
        }

        if (request.FormatScores is { Count: > 0 })
        {
            var ids = request.FormatScores.Select(s => s.CustomFormatId).ToList();
            if (ids.Distinct().Count() != ids.Count)
            {
                return Failure.Invalid("Duplicated custom format score");
            }

            var existing = await db.CustomFormats.Where(f => ids.Contains(f.Id)).Select(f => f.Id).ToListAsync(ct);
            var unknown = ids.Except(existing).ToList();
            if (unknown.Count > 0)
            {
                return Failure.Invalid("Unknown custom format", $"No custom format with id {string.Join(", ", unknown)}.");
            }
        }

        return null;
    }

    private static void Apply(QualityProfile profile, QualityProfileRequest request)
    {
        profile.Items = request.Items.Select(i => new QualityProfileItem
        {
            GroupId = i.QualityIds.Count > 1 || i.GroupId is not null ? i.GroupId : null,
            GroupName = i.QualityIds.Count > 1 || i.GroupId is not null ? i.GroupName?.Trim() : null,
            QualityIds = i.QualityIds.ToList(),
            Allowed = i.Allowed,
        }).ToList();
        profile.Cutoff = request.Cutoff;
        profile.UpgradeAllowed = request.UpgradeAllowed;
        profile.MinFormatScore = request.MinFormatScore;
        profile.CutoffFormatScore = request.CutoffFormatScore;
        profile.MinUpgradeFormatScore = request.MinUpgradeFormatScore;

        var wanted = (request.FormatScores ?? []).Where(s => s.Score != 0).ToDictionary(s => s.CustomFormatId, s => s.Score);
        foreach (var score in profile.FormatScores.ToList())
        {
            if (wanted.TryGetValue(score.CustomFormatId, out var value))
            {
                score.Score = value;
                wanted.Remove(score.CustomFormatId);
            }
            else
            {
                profile.FormatScores.Remove(score);
            }
        }

        foreach (var (customFormatId, value) in wanted)
        {
            profile.FormatScores.Add(new ProfileFormatScore { CustomFormatId = customFormatId, Score = value });
        }
    }

    private async Task<QualityProfileResponse> ToResponseAsync(QualityProfile profile, CancellationToken ct)
    {
        var versionCount = profile.Id == 0 ? 0 : await db.MovieVersions.CountAsync(v => v.QualityProfileId == profile.Id, ct);
        var seriesVersionCount = profile.Id == 0 ? 0 : await db.SeriesVersions.CountAsync(v => v.QualityProfileId == profile.Id, ct);
        return new QualityProfileResponse(
            profile.Id,
            profile.Name,
            profile.Items.Select(i => new QualityProfileItemDto(i.GroupId, i.GroupName, i.QualityIds, i.Allowed)).ToList(),
            profile.Cutoff,
            profile.UpgradeAllowed,
            profile.MinFormatScore,
            profile.CutoffFormatScore,
            profile.MinUpgradeFormatScore,
            profile.PreferredLanguage,
            profile.IsSystem,
            profile.FormatScores.OrderBy(s => s.CustomFormat?.Name).Select(s => new FormatScoreDto(s.CustomFormatId, s.CustomFormat?.Name ?? $"#{s.CustomFormatId}", s.Score)).ToList(),
            versionCount,
            profile.Kind,
            seriesVersionCount);
    }

    private static QualityDefinitionResponse ToResponse(QualityDefinition q) =>
        new(q.Id, q.Name, q.Title, q.Source.ToString(), q.Resolution, q.Modifier.ToString(), q.MinSize, q.PreferredSize, q.MaxSize, q.SeriesMinSize, q.SeriesPreferredSize, q.SeriesMaxSize);
}
