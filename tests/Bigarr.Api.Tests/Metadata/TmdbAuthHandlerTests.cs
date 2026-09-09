using System.Net;
using Bigarr.Api.Infrastructure.Metadata;
using Bigarr.Api.Infrastructure.Metadata.Tmdb;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Tests.Metadata;

public sealed class TmdbAuthHandlerTests
{
    [Fact]
    public async Task Retries_once_after_a_dropped_connection()
    {
        var inner = new SequenceHandler([() => throw new HttpRequestException("reset"), () => new HttpResponseMessage(HttpStatusCode.OK)]);
        using var http = Client(inner);

        var response = await http.GetAsync("movie/603");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Retries_once_on_a_server_error()
    {
        var inner = new SequenceHandler([() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable), () => new HttpResponseMessage(HttpStatusCode.OK)]);
        using var http = Client(inner);

        var response = await http.GetAsync("movie/603");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Gives_up_after_the_retry()
    {
        var inner = new SequenceHandler([() => new HttpResponseMessage(HttpStatusCode.BadGateway), () => new HttpResponseMessage(HttpStatusCode.BadGateway)]);
        using var http = Client(inner);

        var response = await http.GetAsync("movie/603");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Client_errors_are_not_retried()
    {
        var inner = new SequenceHandler([() => new HttpResponseMessage(HttpStatusCode.NotFound)]);
        using var http = Client(inner);

        var response = await http.GetAsync("movie/1");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, inner.Calls);
    }

    private static HttpClient Client(HttpMessageHandler inner)
    {
        var handler = new TmdbAuthHandler(Options.Create(new MetadataOptions { ApiKey = "k" })) { InnerHandler = inner };
        return new HttpClient(handler) { BaseAddress = new Uri("https://api.themoviedb.org/3/") };
    }

    private sealed class SequenceHandler(Func<HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var next = responses[Math.Min(Calls, responses.Length - 1)];
            Calls++;
            return Task.FromResult(next());
        }
    }
}
