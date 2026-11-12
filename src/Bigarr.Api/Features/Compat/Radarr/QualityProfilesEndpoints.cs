using Bigarr.Api.Features.Quality;
using Bigarr.Domain.Quality;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Compat.Radarr;

/// <summary><c>/qualityprofile</c>: Seerr offers them at request time, Bazarr reads <c>language.name</c> as its audio fallback.</summary>
public static class QualityProfilesEndpoints
{
    public static void MapCompatQualityProfiles(this RouteGroupBuilder group)
    {
        group.MapGet("/qualityprofile", async Task<JsonHttpResult<List<RadarrQualityProfile>>> (QualityProfileService profiles, CancellationToken ct) =>
        {
            var definitions = (await profiles.ListDefinitionsAsync(ct)).ToDictionary(d => d.Id);
            var result = new List<RadarrQualityProfile>();
            // Radarr's clients only see profiles a film version can take.
            foreach (var summary in (await profiles.ListAsync(ct)).Where(p => p.Kind.ForMovies()))
            {
                var profile = await profiles.GetAsync(summary.Id, ct);
                if (profile is not null)
                {
                    result.Add(ToResource(profile, definitions));
                }
            }

            return RadarrCompat.Ok(result);
        });

        group.MapGet("/qualityprofile/{id:int}", async Task<Results<JsonHttpResult<RadarrQualityProfile>, NotFound>> (int id, QualityProfileService profiles, CancellationToken ct) =>
        {
            var profile = await profiles.GetAsync(id, ct);
            if (profile is null || !profile.Kind.ForMovies())
            {
                return TypedResults.NotFound();
            }

            var definitions = (await profiles.ListDefinitionsAsync(ct)).ToDictionary(d => d.Id);
            return RadarrCompat.Ok(ToResource(profile, definitions));
        });

        // Radarr's alias; Seerr calls the camelCase spelling, ASP.NET Core routes are case-insensitive anyway.
        group.MapGet("/profile", async Task<JsonHttpResult<List<RadarrQualityProfile>>> (QualityProfileService profiles, CancellationToken ct) =>
        {
            var definitions = (await profiles.ListDefinitionsAsync(ct)).ToDictionary(d => d.Id);
            var result = new List<RadarrQualityProfile>();
            // Radarr's clients only see profiles a film version can take.
            foreach (var summary in (await profiles.ListAsync(ct)).Where(p => p.Kind.ForMovies()))
            {
                var profile = await profiles.GetAsync(summary.Id, ct);
                if (profile is not null)
                {
                    result.Add(ToResource(profile, definitions));
                }
            }

            return RadarrCompat.Ok(result);
        });
    }

    public static RadarrQualityProfile ToResource(QualityProfileResponse profile, IReadOnlyDictionary<int, QualityDefinitionResponse> definitions)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(definitions);

        var items = profile.Items.Select(item => item.GroupId is { } groupId
            ? new RadarrQualityProfileItem(null, item.QualityIds.Select(id => new RadarrQualityProfileItem(Definition(definitions, id), [], item.Allowed)).ToList(), item.Allowed, groupId, item.GroupName)
            : new RadarrQualityProfileItem(Definition(definitions, item.QualityIds.FirstOrDefault()), [], item.Allowed)).ToList();

        return new RadarrQualityProfile(
            profile.Id,
            profile.Name,
            profile.UpgradeAllowed,
            profile.Cutoff,
            items,
            profile.MinFormatScore,
            profile.CutoffFormatScore,
            profile.MinUpgradeFormatScore,
            profile.FormatScores.Select(s => new RadarrFormatItem(s.CustomFormatId, s.CustomFormat, s.Score)).ToList(),
            RadarrMapper.OriginalLanguage(profile.PreferredLanguage) ?? new RadarrLanguage(0, profile.PreferredLanguage));
    }

    private static RadarrQualityDefinition Definition(IReadOnlyDictionary<int, QualityDefinitionResponse> definitions, int id) =>
        definitions.TryGetValue(id, out var d)
            ? new RadarrQualityDefinition(d.Id, d.Name, d.Source.ToLowerInvariant(), d.Resolution, RadarrMapper.Camel(d.Modifier))
            : new RadarrQualityDefinition(id, $"#{id}", "unknown", 0, "none");
}
