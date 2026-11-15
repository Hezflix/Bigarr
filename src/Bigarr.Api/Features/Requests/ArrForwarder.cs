using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bigarr.Domain.Requests;
using Bigarr.Domain.Shadow;
using Bigarr.Domain.Tv;

namespace Bigarr.Api.Features.Requests;

/// <summary>What a forward came to: forwarded, already there, or failed with a message for the approver.</summary>
public sealed record ForwardOutcome(ForwardStatus Status, string? Error = null);

/// <summary>
/// Forward mode: an approved request also goes to the Radarr or Sonarr Bigarr follows for its version, through that app's
/// v3 API and with the payload Seerr would send (mapped profile and root folder, monitored, search on add). A title the
/// app already has counts as forwarded: an unmonitored film is monitored and searched, a series gets the requested seasons
/// monitored and a series search, as Seerr does. The only writes Bigarr ever makes to a followed app.
/// </summary>
public sealed class ArrForwarder(IHttpClientFactory httpClientFactory, ILogger<ArrForwarder> logger)
{
    public const string HttpClientName = "bigarr-request-forward";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ForwardOutcome> ForwardMovieAsync(FollowedRadarr radarr, int tmdbId, string title, int? year, int bigarrProfileId, int bigarrRootFolderId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(radarr);
        var profile = Inverse(radarr.ProfileMap, bigarrProfileId);
        var root = Inverse(radarr.RootFolderMap, bigarrRootFolderId);
        if (profile is null || root is null)
        {
            return Unmapped("Radarr", radarr.Name, profile is null);
        }

        var app = new App("Radarr", radarr.Name, radarr.Url, radarr.ApiKey);
        try
        {
            var lookup = await SendAsync(app, HttpMethod.Get, $"/movie/lookup/tmdb?tmdbId={tmdbId}", null, ct);
            if (lookup is JsonObject found && Id(found) is { } existingId)
            {
                if (found["monitored"]?.GetValue<bool>() != true)
                {
                    var full = await SendAsync(app, HttpMethod.Get, $"/movie/{existingId}", null, ct) as JsonObject ?? found;
                    full["monitored"] = true;
                    await SendAsync(app, HttpMethod.Put, $"/movie/{existingId}", full, ct);
                    await SendAsync(app, HttpMethod.Post, "/command", new JsonObject { ["name"] = "MoviesSearch", ["movieIds"] = new JsonArray(existingId) }, ct);
                }

                return new ForwardOutcome(ForwardStatus.AlreadyThere);
            }

            // Field for field what Seerr posts.
            var body = new JsonObject
            {
                ["title"] = (lookup as JsonObject)?["title"]?.GetValue<string>() ?? title,
                ["qualityProfileId"] = profile.Value,
                ["profileId"] = profile.Value,
                ["minimumAvailability"] = "released",
                ["tags"] = new JsonArray(),
                ["year"] = (lookup as JsonObject)?["year"]?.GetValue<int>() ?? year ?? 0,
                ["rootFolderPath"] = root,
                ["tmdbId"] = tmdbId,
                ["monitored"] = true,
                ["addOptions"] = new JsonObject { ["searchForMovie"] = true },
            };
            await SendAsync(app, HttpMethod.Post, "/movie", body, ct);
            return new ForwardOutcome(ForwardStatus.Forwarded);
        }
        catch (ForwardException ex) when (ex.AlreadyAdded)
        {
            return new ForwardOutcome(ForwardStatus.AlreadyThere);
        }
        catch (ForwardException ex)
        {
            logger.LogWarning("Forward of TMDB {TmdbId} to Radarr {Name} failed: {Error}", tmdbId, radarr.Name, ex.Message);
            return new ForwardOutcome(ForwardStatus.Failed, ex.Message);
        }
    }

    public async Task<ForwardOutcome> ForwardSeriesAsync(FollowedSonarr sonarr, int tvdbId, string title, IReadOnlyCollection<int> seasons, SeriesType type, int bigarrProfileId, int bigarrRootFolderId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sonarr);
        ArgumentNullException.ThrowIfNull(seasons);
        var profile = Inverse(sonarr.ProfileMap, bigarrProfileId);
        var root = Inverse(sonarr.RootFolderMap, bigarrRootFolderId);
        if (profile is null || root is null)
        {
            return Unmapped("Sonarr", sonarr.Name, profile is null);
        }

        var app = new App("Sonarr", sonarr.Name, sonarr.Url, sonarr.ApiKey);
        try
        {
            var lookup = (await SendAsync(app, HttpMethod.Get, $"/series/lookup?term=tvdb:{tvdbId}", null, ct) as JsonArray)?.OfType<JsonObject>().FirstOrDefault();
            if (lookup is not null && Id(lookup) is { } existingId)
            {
                var full = await SendAsync(app, HttpMethod.Get, $"/series/{existingId}", null, ct) as JsonObject ?? lookup;
                full["monitored"] = true;
                foreach (var season in (full["seasons"] as JsonArray ?? []).OfType<JsonObject>())
                {
                    if (season["seasonNumber"]?.GetValue<int>() is { } number && seasons.Contains(number))
                    {
                        season["monitored"] = true;
                    }
                }

                await SendAsync(app, HttpMethod.Put, $"/series/{existingId}", full, ct);
                await SendAsync(app, HttpMethod.Post, "/command", new JsonObject { ["name"] = "SeriesSearch", ["seriesId"] = existingId }, ct);
                return new ForwardOutcome(ForwardStatus.AlreadyThere);
            }

            // Sonarr v3 wants a language profile; v4 has none and ignores the field.
            int? languageProfile = null;
            try
            {
                languageProfile = (await SendAsync(app, HttpMethod.Get, "/languageprofile", null, ct) as JsonArray)?.OfType<JsonObject>().Select(Id).FirstOrDefault(i => i is not null);
            }
            catch (ForwardException)
            {
                // not a v3
            }

            var known = (lookup?["seasons"] as JsonArray ?? []).OfType<JsonObject>().Select(s => s["seasonNumber"]?.GetValue<int>()).OfType<int>();
            var body = new JsonObject
            {
                ["tvdbId"] = tvdbId,
                ["title"] = lookup?["title"]?.GetValue<string>() ?? title,
                ["qualityProfileId"] = profile.Value,
                ["seasons"] = new JsonArray([.. known.Union(seasons).Order().Select(n => (JsonNode)new JsonObject { ["seasonNumber"] = n, ["monitored"] = seasons.Contains(n) })]),
                ["tags"] = new JsonArray(),
                ["seasonFolder"] = true,
                ["monitored"] = true,
                ["rootFolderPath"] = root,
                ["seriesType"] = type.ToString().ToLowerInvariant(),
                ["addOptions"] = new JsonObject { ["ignoreEpisodesWithFiles"] = true, ["searchForMissingEpisodes"] = true },
            };
            if (languageProfile is not null)
            {
                body["languageProfileId"] = languageProfile.Value;
            }

            await SendAsync(app, HttpMethod.Post, "/series", body, ct);
            return new ForwardOutcome(ForwardStatus.Forwarded);
        }
        catch (ForwardException ex) when (ex.AlreadyAdded)
        {
            return new ForwardOutcome(ForwardStatus.AlreadyThere);
        }
        catch (ForwardException ex)
        {
            logger.LogWarning("Forward of TVDB {TvdbId} to Sonarr {Name} failed: {Error}", tvdbId, sonarr.Name, ex.Message);
            return new ForwardOutcome(ForwardStatus.Failed, ex.Message);
        }
    }

    /// <summary>The app's profile (or root folder) the import mapped to Bigarr's one; the smallest id when several were.</summary>
    private static int? Inverse(Dictionary<int, int> map, int bigarrId) =>
        map.Where(p => p.Value == bigarrId).Select(p => (int?)p.Key).Order().FirstOrDefault();

    private static string? Inverse(Dictionary<string, int> map, int bigarrId) =>
        map.Where(p => p.Value == bigarrId).Select(p => p.Key).Order(StringComparer.Ordinal).FirstOrDefault();

    private static ForwardOutcome Unmapped(string kind, string name, bool profile) => new(
        ForwardStatus.Failed,
        profile
            ? $"No {kind} quality profile of '{name}' is mapped to the request's profile: approve it with a mapped profile, or retry after following again."
            : $"No {kind} root folder of '{name}' is mapped to the request's root folder: approve it with a mapped root folder, or retry after following again.");

    private static int? Id(JsonObject? resource) =>
        resource?["id"] is JsonValue value && value.TryGetValue<int>(out var id) && id > 0 ? id : null;

    private sealed record App(string Kind, string Name, string Url, string ApiKey)
    {
        public string BaseUrl
        {
            get
            {
                var url = Url.Trim().TrimEnd('/');
                return url.EndsWith("/api/v3", StringComparison.OrdinalIgnoreCase) ? url[..^"/api/v3".Length] : url;
            }
        }
    }

    private sealed class ForwardException(string message, bool alreadyAdded = false) : Exception(message)
    {
        public bool AlreadyAdded { get; } = alreadyAdded;
    }

    private async Task<JsonNode?> SendAsync(App app, HttpMethod method, string path, JsonNode? body, CancellationToken ct)
    {
        if (!Uri.TryCreate(app.BaseUrl, UriKind.Absolute, out var baseUri) || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ForwardException($"{app.Kind} '{app.Name}' has no usable URL.");
        }

        using var request = new HttpRequestMessage(method, app.BaseUrl + "/api/v3" + path);
        request.Headers.TryAddWithoutValidation("X-Api-Key", app.ApiKey.Trim());
        request.Headers.Accept.ParseAdd("application/json");
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(Json), Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ForwardException(ex is TaskCanceledException ? $"{app.Kind} '{app.Name}' did not answer in time." : $"{app.Kind} '{app.Name}' could not be reached: {ex.Message}");
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new ForwardException($"{app.Kind} '{app.Name}' refused the API key (HTTP {(int)response.StatusCode}).");
            }

            if (!response.IsSuccessStatusCode)
            {
                // Radarr and Sonarr answer a validation error when the title is already there (a race with a pass of the follow).
                var already = response.StatusCode == HttpStatusCode.BadRequest && text.Contains("already been added", StringComparison.OrdinalIgnoreCase);
                throw new ForwardException($"{app.Kind} '{app.Name}' answered HTTP {(int)response.StatusCode} on {method} {path.Split('?')[0]}{Detail(text)}", already);
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            try
            {
                return JsonNode.Parse(text);
            }
            catch (JsonException)
            {
                throw new ForwardException($"{app.Kind} '{app.Name}' answered something that is not JSON on {path.Split('?')[0]}.");
            }
        }
    }

    /// <summary>The first validation message of an error body, when it has one.</summary>
    private static string Detail(string body)
    {
        try
        {
            var node = JsonNode.Parse(body);
            var message = node switch
            {
                JsonArray array => array.OfType<JsonObject>().Select(e => e["errorMessage"]?.GetValue<string>()).FirstOrDefault(m => m is not null),
                JsonObject obj => obj["message"]?.GetValue<string>(),
                _ => null,
            };
            return message is null ? "." : $": {message}";
        }
        catch (JsonException)
        {
            return ".";
        }
    }
}
