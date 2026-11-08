using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Domain.History;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Compat.CompatTestHelpers;

namespace Bigarr.Api.Tests.Compat;

/// <summary>
/// <c>/api/v3/history</c> is sorted, counted and paged by the database: pages, order and totals must be what an in-memory
/// pass over the same events gives, for both directions, the key's view, the filters and the edges.
/// </summary>
public sealed class CompatHistoryPagingTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    private static readonly HistoryEventType[] Types =
        [HistoryEventType.Grabbed, HistoryEventType.Imported, HistoryEventType.Upgraded, HistoryEventType.DownloadFailed, HistoryEventType.Blocklisted];

    private sealed record Seeded(int Id, int? Target, HistoryEventType Type, DateTime At);

    [Fact]
    public async Task Pages_order_and_totals_match_the_whole_journal_sorted_in_memory()
    {
        var session = await SessionAsync(factory);
        var (hd, uhd, root) = await SetupAsync(session);
        var (plain, _) = await KeyClientAsync(factory, session, "paging-plain", "default");
        var (seerr4k, _) = await KeyClientAsync(factory, session, "paging-4k", "version:4k");
        var (bazarr, _) = await KeyClientAsync(factory, session, "paging-all", "all");

        // Film one has two versions (1080p default, 4k); film two only the default one.
        var film1Hd = await AddAsync(plain, SeerrAdd(603, "The Matrix", 1999, hd, root.Path));
        var film1Uhd = await AddAsync(seerr4k, SeerrAdd(603, "The Matrix", 1999, uhd, root.Path));
        var film2Hd = await AddAsync(plain, SeerrAdd(27205, "Inception", 2010, hd, root.Path));

        // Ties on the date (every third event shares its neighbour's minute) exercise the id tie-break.
        var origin = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var targets = new int?[] { film1Hd, film1Hd, film1Uhd, film2Hd, null, film2Hd };
        var seeded = new List<Seeded>();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            for (var i = 0; i < 90; i++)
            {
                var target = targets[i % targets.Length];
                var e = new HistoryEvent { TargetId = target, EventType = Types[i % Types.Length], SourceTitle = $"Release.{i}.1080p.WEB-DL-GRP", CreatedAt = origin.AddMinutes(i - i / 3) };
                db.History.Add(e);
                seeded.Add(new Seeded(0, target, e.EventType, e.CreatedAt));
            }

            await db.SaveChangesAsync();
            seeded = db.History.Where(h => h.CreatedAt >= origin).AsEnumerable().Select(h => new Seeded(h.Id, h.TargetId, h.EventType, h.CreatedAt)).ToList();
        }

        string Name(HistoryEventType t) => Features.Compat.Radarr.HistoryEndpoints.EventType(t);

        async Task CheckAsync(HttpClient client, string query, IEnumerable<Seeded> universe, bool ascending, int page, int pageSize)
        {
            var all = ascending ? universe.OrderBy(s => s.At).ThenBy(s => s.Id).ToList() : universe.OrderByDescending(s => s.At).ThenByDescending(s => s.Id).ToList();
            var direction = ascending ? "ascending" : "descending";
            var body = await (await client.GetAsync($"/api/v3/history?page={page}&pageSize={pageSize}&sortKey=date&sortDirection={direction}{query}")).ReadJsonAsync();
            Assert.Equal(all.Count, body.GetProperty("totalRecords").GetInt32());
            Assert.Equal(page, body.GetProperty("page").GetInt32());
            Assert.Equal(pageSize, body.GetProperty("pageSize").GetInt32());
            var expected = all.Skip((page - 1) * pageSize).Take(pageSize).Select(s => s.Id).ToList();
            Assert.Equal(expected, body.GetProperty("records").EnumerateArray().Select(r => r.GetProperty("id").GetInt32()).ToList());
        }

        var mine = seeded.Where(s => s.Target is not null).ToList();
        var viewDefault = mine.Where(s => s.Target == film1Hd || s.Target == film2Hd).ToList();
        var only4k = mine.Where(s => s.Target == film1Uhd).ToList();

        // Every line of the "all" key, several pages, both directions, including the short last page and one past the end.
        foreach (var ascending in new[] { false, true })
        {
            foreach (var page in new[] { 1, 2, 3, 4 })
            {
                await CheckAsync(bazarr, string.Empty, mine, ascending, page, 25);
            }
        }

        // Views: the default key never sees the 4k line, the 4k key only its own.
        await CheckAsync(plain, string.Empty, viewDefault, false, 2, 20);
        await CheckAsync(seerr4k, string.Empty, only4k, true, 1, 20);

        // Filters: the Radarr numeric type and its name, one version, a list of versions, and a type that maps to two of ours.
        await CheckAsync(bazarr, $"&eventType=1&movieIds={film1Hd}", mine.Where(s => s.Target == film1Hd && s.Type == HistoryEventType.Grabbed), false, 1, 50);
        await CheckAsync(bazarr, "&eventType=downloadFolderImported", mine.Where(s => Name(s.Type) == "downloadFolderImported"), false, 2, 10);
        await CheckAsync(bazarr, $"&movieIds={film1Uhd},{film2Hd}", mine.Where(s => s.Target == film1Uhd || s.Target == film2Hd), true, 1, 15);

        // Asking for a line the key does not see matches nothing; it does not widen to the whole view.
        await CheckAsync(plain, $"&movieIds={film1Uhd}", [], false, 1, 20);

        // A page past the end is empty but keeps the total; paging parameters are clamped.
        var past = await (await bazarr.GetAsync("/api/v3/history?page=99&pageSize=25")).ReadJsonAsync();
        Assert.Equal(mine.Count, past.GetProperty("totalRecords").GetInt32());
        Assert.Empty(past.GetProperty("records").EnumerateArray());
        var clamped = await (await bazarr.GetAsync("/api/v3/history?page=0&pageSize=5000")).ReadJsonAsync();
        Assert.Equal(1, clamped.GetProperty("page").GetInt32());
        Assert.Equal(1000, clamped.GetProperty("pageSize").GetInt32());
        Assert.Equal(mine.Count, clamped.GetProperty("records").GetArrayLength());

        // The per-movie variant: newest first, type filter applied by the database.
        var movie = await (await bazarr.GetAsync($"/api/v3/history/movie?movieId={film1Hd}&eventType=1")).ReadJsonAsync();
        Assert.Equal(
            mine.Where(s => s.Target == film1Hd && s.Type == HistoryEventType.Grabbed).OrderByDescending(s => s.At).ThenByDescending(s => s.Id).Select(s => s.Id),
            movie.EnumerateArray().Select(r => r.GetProperty("id").GetInt32()));
    }

    [Fact]
    public async Task An_empty_journal_gives_an_empty_page()
    {
        var session = await SessionAsync(factory);
        var (bazarr, _) = await KeyClientAsync(factory, session, "paging-empty", "all");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        db.History.RemoveRange(db.History);
        await db.SaveChangesAsync();

        var body = await (await bazarr.GetAsync("/api/v3/history?page=1&pageSize=20")).ReadJsonAsync();
        Assert.Equal(0, body.GetProperty("totalRecords").GetInt32());
        Assert.Empty(body.GetProperty("records").EnumerateArray());
    }

    private static async Task<int> AddAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/v3/movie", body);
        Assert.True(response.IsSuccessStatusCode);
        return (await response.ReadJsonAsync()).GetProperty("id").GetInt32();
    }
}
