using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace Bigarr.Api.Infrastructure.Xem;

/// <summary>One XEM row: how the scene numbers an episode, next to its TVDB numbers.</summary>
public sealed record XemMapping(int SceneSeason, int SceneEpisode, int SceneAbsolute, int TvdbSeason, int TvdbEpisode, int TvdbAbsolute);

/// <summary>A name the scene uses for a show; <see cref="SceneSeasonNumber"/> null when it covers every season.</summary>
public sealed record XemName(int TvdbId, string Title, int? SceneSeasonNumber);

/// <summary>TheXEM's read API (thexem.info, keyless), the three calls Sonarr makes. Failures surface as exceptions.</summary>
public interface IXemClient
{
    Task<IReadOnlyList<int>> GetMappedTvdbIdsAsync(CancellationToken ct);

    Task<IReadOnlyList<XemMapping>> GetMappingsAsync(int tvdbId, CancellationToken ct);

    Task<IReadOnlyList<XemName>> GetNamesAsync(CancellationToken ct);
}

/// <summary>
/// HTTP client for <c>/map/havemap</c>, <c>/map/all</c> and <c>/map/allNames</c> with <c>origin=tvdb</c>, read the way
/// Sonarr's <c>XemProxy</c> reads them (develop c018578, GPL-3.0). Never Sonarr's own services.
/// </summary>
public sealed class XemClient(HttpClient http) : IXemClient
{
    public const string HttpClientName = "xem";

    private static readonly string[] IgnoredErrors = ["no single connection", "no show with the tvdb_id"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<int>> GetMappedTvdbIdsAsync(CancellationToken ct)
    {
        var response = await GetAsync<List<string>>("havemap?origin=tvdb", ct);
        return (response ?? [])
            .Select(id => int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0)
            .Where(id => id > 0)
            .ToList();
    }

    public async Task<IReadOnlyList<XemMapping>> GetMappingsAsync(int tvdbId, CancellationToken ct)
    {
        var rows = await GetAsync<List<MappingRow>>($"all?id={tvdbId}&origin=tvdb", ct);
        return (rows ?? [])
            .Where(r => r.Scene is not null && r.Tvdb is not null)
            .Select(r => new XemMapping(r.Scene!.Season, r.Scene.Episode, r.Scene.Absolute, r.Tvdb!.Season, r.Tvdb.Episode, r.Tvdb.Absolute))
            .ToList();
    }

    public async Task<IReadOnlyList<XemName>> GetNamesAsync(CancellationToken ct)
    {
        var data = await GetAsync<Dictionary<string, List<Dictionary<string, JsonElement>>>>("allNames?origin=tvdb&seasonNumbers=1", ct);
        var names = new List<XemName>();
        foreach (var (key, entries) in data ?? [])
        {
            if (!int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var tvdbId))
            {
                continue;
            }

            foreach (var (title, value) in entries.SelectMany(e => e))
            {
                if (string.IsNullOrWhiteSpace(title) || !TryReadInt(value, out var season))
                {
                    continue;
                }

                // Sonarr's hack for Fate/Zero, whose later "seasons" are other shows.
                if (tvdbId == 79151 && season > 1)
                {
                    continue;
                }

                names.Add(new XemName(tvdbId, title.Trim(), season < 0 ? null : season));
            }
        }

        return names;
    }

    private async Task<T?> GetAsync<T>(string resource, CancellationToken ct)
    {
        var result = await http.GetFromJsonAsync<XemResult<T>>(resource, Json, ct)
                     ?? throw new HttpRequestException($"Empty answer from TheXEM for {resource}.");

        if (string.Equals(result.Result, "failure", StringComparison.OrdinalIgnoreCase))
        {
            if (IgnoredErrors.Any(e => result.Message?.Contains(e, StringComparison.OrdinalIgnoreCase) == true))
            {
                return default;
            }

            throw new HttpRequestException($"TheXEM answered a failure for {resource}: {result.Message}");
        }

        return result.Data;
    }

    private static bool TryReadInt(JsonElement value, out int number)
    {
        number = 0;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt32(out number),
            JsonValueKind.String => int.TryParse(value.GetString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number),
            _ => false,
        };
    }

    private sealed record XemResult<T>(string? Result, T? Data, string? Message);

    private sealed record MappingRow(NumberSet? Scene, NumberSet? Tvdb);

    private sealed record NumberSet(int Season, int Episode, int Absolute);
}
