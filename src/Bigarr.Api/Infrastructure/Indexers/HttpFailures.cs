using System.Net;
using Bigarr.Domain.Indexers;

namespace Bigarr.Api.Infrastructure.Indexers;

/// <summary>HTTP status handling of the indexer clients: every failure becomes a typed <see cref="IndexerException"/>.</summary>
public static class HttpFailures
{
    public static void ThrowIfFailed(HttpResponseMessage response, string who)
    {
        ArgumentNullException.ThrowIfNull(response);

        var status = (int)response.StatusCode;
        if (status is >= 300 and < 400)
        {
            // An SSO proxy (oauth2-proxy, Authelia…) in front of the indexer answers every path with a redirect to its login page.
            var target = response.Headers.Location?.Host;
            throw new IndexerException(
                IndexerFailureKind.Authentication,
                target is null
                    ? $"{who} redirected the request (HTTP {status}): a login page is probably in front of it."
                    : $"{who} redirected to {target} (HTTP {status}): a login page is in front of it. Exempt its API paths from the SSO, or use an internal URL.");
        }

        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                throw new IndexerException(IndexerFailureKind.Authentication, $"HTTP {status}: {who} refused the request. Check the URL and the API key.");
            case HttpStatusCode.TooManyRequests:
                throw new IndexerException(IndexerFailureKind.RateLimited, $"HTTP 429: {who} is rate limiting us.");
            case >= HttpStatusCode.InternalServerError:
                throw new IndexerException(IndexerFailureKind.ServerError, $"HTTP {status}: {who} failed.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new IndexerException(IndexerFailureKind.Request, $"HTTP {status} from {who}.");
        }
    }
}
