using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Bigarr.Api.Tests.Hosting;
using Microsoft.AspNetCore.Http;

namespace Bigarr.Api.Tests.Playback;

/// <summary>
/// A Plex server with invented users and films: <c>/status/sessions</c> from <see cref="Streams"/>, metadata from
/// <see cref="Items"/>, two film libraries (1080p and 4K folders), and the server-sent event stream fed by
/// <see cref="SendAsync"/>.
/// </summary>
public sealed class FakePlex : IAsyncDisposable
{
    public const string Token = "fake-plex-token";

    private readonly FakeHttpServer _http;
    private readonly Lock _lock = new();
    private Channel<string>? _events;

    private FakePlex(FakeHttpServer http)
    {
        _http = http;
        http.Responder = Respond;
    }

    public sealed record Part(string Id, string? File, string Resolution, string VideoCodec = "hevc", string? ColorTrc = null);

    public sealed record Item(
        string RatingKey,
        string Title,
        int Year,
        long DurationMs,
        string SectionId,
        IReadOnlyList<Part> Parts,
        int? TmdbId = null,
        string Type = "movie",
        long? CreditsMs = null,
        string? ShowRatingKey = null,
        string? ShowTitle = null,
        int? Season = null,
        int? Episode = null);

    public sealed record Stream(
        string SessionKey,
        string SessionId,
        string RatingKey,
        string UserId,
        string UserName,
        string PartId,
        string State = "playing",
        long ViewOffsetMs = 0,
        bool Transcode = false,
        string Player = "Living room TV",
        string Address = "192.0.2.10",
        bool Local = false);

    public string Url => _http.Url;

    public List<Stream> Streams { get; } = [];

    public Dictionary<string, Item> Items { get; } = [];

    /// <summary>Shows' TVDB ids, by rating key.</summary>
    public Dictionary<string, int> ShowTvdbIds { get; } = [];

    /// <summary>Refuse the event stream (HTTP 500) while the rest answers: Bigarr must fall back to polling.</summary>
    public bool EventsDown { get; set; }

    public bool IsListening
    {
        get
        {
            lock (_lock)
            {
                return _events is not null;
            }
        }
    }

    public IEnumerable<FakeHttpServer.Received> Requests => _http.Requests;

    public static async Task<FakePlex> StartAsync() => new(await FakeHttpServer.StartAsync());

    public void Set(Stream stream)
    {
        lock (_lock)
        {
            Streams.RemoveAll(s => s.SessionKey == stream.SessionKey);
            Streams.Add(stream);
        }
    }

    public void Remove(string sessionKey)
    {
        lock (_lock)
        {
            Streams.RemoveAll(s => s.SessionKey == sessionKey);
        }
    }

    public async Task SendAsync(string sessionKey, string ratingKey, string state, long viewOffsetMs)
    {
        var data = JsonSerializer.Serialize(new
        {
            PlaySessionStateNotification = new { sessionKey, clientIdentifier = "fake-player", guid = string.Empty, ratingKey, url = string.Empty, key = $"/library/metadata/{ratingKey}", viewOffset = viewOffsetMs, playQueueItemID = 1, playQueueID = 1, state },
        });
        Channel<string>? events;
        lock (_lock)
        {
            events = _events;
        }

        if (events is null)
        {
            throw new InvalidOperationException("Nobody listens to the fake Plex events.");
        }

        await events.Writer.WriteAsync($"event: playing\ndata: {data}\n\n");
    }

    public async Task WaitForListenerAsync(TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (!IsListening)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Bigarr never opened the event stream.");
            }

            await Task.Delay(50);
        }
    }

    private FakeHttpServer.Answer? Respond(FakeHttpServer.Received request)
    {
        if (!request.Headers.TryGetValue("X-Plex-Token", out var token) || token != Token)
        {
            return new FakeHttpServer.Answer(401);
        }

        if (request.Path == "/:/eventsource/notifications")
        {
            return EventsDown ? new FakeHttpServer.Answer(500) : new FakeHttpServer.Answer(200, Stream: StreamEventsAsync);
        }

        lock (_lock)
        {
            if (request.Path == "/status/sessions")
            {
                return Json(new { MediaContainer = new { size = Streams.Count, Metadata = Streams.Select(SessionJson).ToList() } });
            }

            if (request.Path == "/library/sections")
            {
                return Json(new
                {
                    MediaContainer = new
                    {
                        Directory = new object[]
                        {
                            new { key = "3", title = "Films", type = "movie", Location = new[] { new { id = 1, path = "/media/movies" } } },
                            new { key = "7", title = "Films 4K", type = "movie", Location = new[] { new { id = 2, path = "/media/movies4k" } } },
                            new { key = "4", title = "Shows", type = "show", Location = new[] { new { id = 3, path = "/media/tv" } } },
                        },
                    },
                });
            }

            if (request.Path.StartsWith("/library/metadata/", StringComparison.Ordinal))
            {
                var key = request.Path["/library/metadata/".Length..];
                if (Items.TryGetValue(key, out var item))
                {
                    return Json(new { MediaContainer = new { Metadata = new[] { ItemJson(item) } } });
                }

                if (ShowTvdbIds.TryGetValue(key, out var tvdb))
                {
                    return Json(new { MediaContainer = new { Metadata = new[] { new { ratingKey = key, type = "show", guid = $"plex://show/{key}", Guid = new[] { new { id = $"tvdb://{tvdb}" } } } } } });
                }

                return new FakeHttpServer.Answer(404);
            }
        }

        return null;
    }

    private async Task StreamEventsAsync(HttpResponse response, CancellationToken ct)
    {
        var events = Channel.CreateUnbounded<string>();
        lock (_lock)
        {
            _events = events;
        }

        try
        {
            response.ContentType = "text/event-stream";
            await response.WriteAsync("event: ping\ndata: {}\n\n", ct);
            await response.Body.FlushAsync(ct);
            await foreach (var message in events.Reader.ReadAllAsync(ct))
            {
                await response.Body.WriteAsync(Encoding.UTF8.GetBytes(message), ct);
                await response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Bigarr hung up
        }
        finally
        {
            lock (_lock)
            {
                if (_events == events)
                {
                    _events = null;
                }
            }
        }
    }

    private object SessionJson(Stream stream)
    {
        var item = Items[stream.RatingKey];
        var part = item.Parts.First(p => p.Id == stream.PartId);
        return new
        {
            sessionKey = stream.SessionKey,
            ratingKey = stream.RatingKey,
            type = item.Type,
            title = item.Title,
            duration = item.DurationMs,
            viewOffset = stream.ViewOffsetMs,
            librarySectionID = item.SectionId,
            Media = new[]
            {
                new
                {
                    id = "m" + part.Id,
                    videoResolution = stream.Transcode ? "720" : part.Resolution,
                    videoCodec = stream.Transcode ? "h264" : part.VideoCodec,
                    audioCodec = "eac3",
                    audioChannels = 6,
                    container = "mkv",
                    bitrate = stream.Transcode ? 4000 : 20000,
                    selected = true,
                    Part = new[] { new { id = part.Id, decision = stream.Transcode ? "transcode" : "directplay", selected = true } },
                },
            },
            User = new { id = stream.UserId, title = stream.UserName, thumb = $"https://example.invalid/users/{stream.UserId}.png" },
            Player = new { title = stream.Player, product = "Plex for Android (TV)", platform = "Android", platformVersion = "12", device = "TV", machineIdentifier = "player-" + stream.SessionKey, address = stream.Address, local = stream.Local, secure = true, relayed = false, state = stream.State },
            Session = new { id = stream.SessionId, bandwidth = 21000, location = stream.Local ? "lan" : "wan" },
            TranscodeSession = stream.Transcode ? new { videoDecision = "transcode", audioDecision = "copy", transcodeHwFullPipeline = true, transcodeHwEncodingTitle = "Intel (QuickSync)", container = "mpegts", videoCodec = "h264", audioCodec = "eac3", audioChannels = 6, speed = 1.8, throttled = true, maxOffsetAvailable = (stream.ViewOffsetMs + 90_000) / 1000.0, protocol = "dash" } : null,
        };
    }

    private static object ItemJson(Item item) => new
    {
        ratingKey = item.RatingKey,
        type = item.Type,
        title = item.Title,
        year = item.Year,
        duration = item.DurationMs,
        guid = item.Type == "movie" ? $"plex://movie/fake{item.RatingKey}" : $"plex://episode/fake{item.RatingKey}",
        librarySectionID = item.SectionId,
        librarySectionTitle = item.SectionId == "7" ? "Films 4K" : item.Type == "movie" ? "Films" : "Shows",
        grandparentRatingKey = item.ShowRatingKey,
        grandparentTitle = item.ShowTitle,
        parentRatingKey = item.ShowRatingKey is null ? null : item.ShowRatingKey + "-s",
        parentIndex = item.Season,
        index = item.Episode,
        Guid = item.TmdbId is { } tmdb ? new[] { new { id = $"tmdb://{tmdb}" }, new { id = "imdb://tt0000001" } } : [],
        Marker = item.CreditsMs is { } credits ? new[] { new { type = "credits", startTimeOffset = credits, endTimeOffset = item.DurationMs } } : [],
        Media = item.Parts.Select(p => new
        {
            id = "m" + p.Id,
            videoResolution = p.Resolution,
            videoCodec = p.VideoCodec,
            audioCodec = "eac3",
            audioChannels = 6,
            container = "mkv",
            bitrate = 20000,
            Part = new[]
            {
                new
                {
                    id = p.Id,
                    file = p.File,
                    Stream = new object[] { new { streamType = 1, codec = p.VideoCodec, colorTrc = p.ColorTrc }, new { streamType = 2, codec = "eac3" } },
                },
            },
        }).ToList(),
    };

    private static FakeHttpServer.Answer Json(object body) => new(200, JsonSerializer.Serialize(body));

    public async ValueTask DisposeAsync() => await _http.DisposeAsync();
}
