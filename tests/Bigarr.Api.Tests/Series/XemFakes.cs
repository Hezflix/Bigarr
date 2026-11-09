using System.Net;
using System.Reflection;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Infrastructure.Xem;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Tests.Series;

/// <summary>
/// TheXEM answers recorded on 2026-11-09 from thexem.info (<c>Series/Xem/*.json</c>): Frieren's full mapping (its
/// second season restarts the scene absolute count at 1 while TVDB goes on at 29), a trimmed <c>havemap</c> and a
/// trimmed <c>allNames</c>. Served through the real <see cref="XemClient"/> so its parsing is under test too.
/// </summary>
public static class XemFakes
{
    public const int FrierenTvdbId = 424536;

    public static XemClient RecordedClient() => new(new HttpClient(new RecordedHandler()) { BaseAddress = new Uri("https://xem.test/map/") });

    public static XemClient UnreachableClient() => new(new HttpClient(new UnreachableHandler()) { BaseAddress = new Uri("https://xem.test/map/") });

    public static XemService Service(BigarrDbContext db, TimeProvider time, IXemClient client, bool enabled = true) =>
        new(db, client, new XemMappedIds(time), Options.Create(new XemOptions { Enabled = enabled }), time, NullLogger<XemService>.Instance);

    public static XemService Disabled(BigarrDbContext db, TimeProvider time) => Service(db, time, UnreachableClient(), enabled: false);

    public static string Read(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Xem.{name}.json")
                           ?? throw new InvalidOperationException($"No recorded XEM answer '{name}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Serves the recordings by route; any other show gets XEM's own "no show" failure.</summary>
    public sealed class RecordedHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Requests.Add(uri.PathAndQuery);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var body = uri.AbsolutePath switch
            {
                "/map/havemap" => Read("havemap"),
                "/map/allNames" => Read("allNames"),
                "/map/all" when query["id"] == FrierenTvdbId.ToString() => Read("all-424536"),
                "/map/all" => $$"""{"result":"failure","data":[],"message":"no show with the tvdb_id {{query["id"]}} found"}""",
                _ => null,
            };

            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }

    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection refused (xem.test:443)");
    }
}
