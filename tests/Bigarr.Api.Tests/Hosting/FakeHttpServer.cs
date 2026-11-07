using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bigarr.Api.Tests.Hosting;

/// <summary>
/// A tiny HTTP server on a random local port that plays another app (a Radarr, a Plex…): the test decides the answer
/// per request and reads back everything that was asked.
/// </summary>
public sealed class FakeHttpServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    public sealed record Received(string Method, string Path, string Query, IReadOnlyDictionary<string, string> Headers, string Body);

    /// <summary>
    /// An answer: status code, an optional JSON body and optional headers (<c>Retry-After</c>…). With <paramref name="Gzip"/>
    /// the body is gzipped when the request accepts it, like a Radarr behind its own compression does.
    /// </summary>
    public sealed record Answer(int StatusCode, string? Json = null, IReadOnlyDictionary<string, string>? Headers = null, bool Gzip = false);

    private FakeHttpServer(WebApplication app)
    {
        _app = app;
    }

    private long _bytesSent;

    /// <summary>Bytes of response bodies sent so far, as they went over the wire (gzipped when they were).</summary>
    public long BytesSent => Interlocked.Read(ref _bytesSent);

    public ConcurrentQueue<Received> Requests { get; } = new();

    public string Url => _app.Urls.First();

    /// <summary>The test's routing: method and path in, answer out. Null means 404.</summary>
    public Func<Received, Answer?> Responder { get; set; } = _ => null;

    public static async Task<FakeHttpServer> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var server = new FakeHttpServer(app);
        app.Run(async context =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync();
            var headers = context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
            var received = new Received(context.Request.Method, context.Request.Path.Value ?? "/", context.Request.QueryString.Value ?? string.Empty, headers, body);
            server.Requests.Enqueue(received);
            var answer = server.Responder(received) ?? new Answer(404);
            context.Response.StatusCode = answer.StatusCode;
            foreach (var (name, value) in answer.Headers ?? new Dictionary<string, string>())
            {
                context.Response.Headers[name] = value;
            }

            if (answer.Json is not null)
            {
                context.Response.ContentType = "application/json";
                var payload = Encoding.UTF8.GetBytes(answer.Json);
                if (answer.Gzip && headers.TryGetValue("Accept-Encoding", out var accepted) && accepted.Contains("gzip", StringComparison.OrdinalIgnoreCase))
                {
                    using var packed = new MemoryStream();
                    await using (var gzip = new GZipStream(packed, CompressionLevel.Fastest, leaveOpen: true))
                    {
                        await gzip.WriteAsync(payload);
                    }

                    payload = packed.ToArray();
                    context.Response.Headers.ContentEncoding = "gzip";
                }

                Interlocked.Add(ref server._bytesSent, payload.Length);
                await context.Response.Body.WriteAsync(payload);
            }
        });
        await app.StartAsync();
        return server;
    }

    public async Task<Received> WaitForAsync(Func<Received, bool> match, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (true)
        {
            if (Requests.FirstOrDefault(match) is { } hit)
            {
                return hit;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"No request matched. Received: {string.Join(" | ", Requests.Select(r => $"{r.Method} {r.Path}{r.Query}"))}");
            }

            await Task.Delay(100);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
