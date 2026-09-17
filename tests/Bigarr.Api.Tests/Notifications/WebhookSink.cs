using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bigarr.Api.Tests.Notifications;

/// <summary>A tiny HTTP receiver on a random local port: records every webhook and answers what the test decides.</summary>
public sealed class WebhookSink : IAsyncDisposable
{
    private readonly WebApplication _app;

    public sealed record Received(string EventType, string DeliveryId, string? Signature, string Body, DateTime ReceivedAt);

    private WebhookSink(WebApplication app)
    {
        _app = app;
    }

    public ConcurrentQueue<Received> Requests { get; } = new();

    /// <summary>What the sink answers; 200 by default.</summary>
    public int StatusCode { get; set; } = 200;

    public string Url => $"{_app.Urls.First()}/hook";

    public static async Task<WebhookSink> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var sink = new WebhookSink(app);
        app.MapPost("/hook", async (HttpRequest request) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            sink.Requests.Enqueue(new Received(
                request.Headers["X-Bigarr-Event"].ToString(),
                request.Headers["X-Bigarr-Delivery"].ToString(),
                request.Headers.TryGetValue("X-Bigarr-Signature", out var signature) ? signature.ToString() : null,
                body,
                DateTime.UtcNow));
            return Results.StatusCode(sink.StatusCode);
        });
        await app.StartAsync();
        return sink;
    }

    public async Task<Received> WaitForAsync(Func<Received, bool> match, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (true)
        {
            var hit = Requests.FirstOrDefault(match);
            if (hit is not null)
            {
                return hit;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"No webhook matched. Received: {string.Join(" | ", Requests.Select(r => $"{r.EventType}#{r.DeliveryId}"))}");
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
