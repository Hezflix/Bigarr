using Bigarr.Api.Features.Discover;
using Bigarr.Domain.Requests;

namespace Bigarr.Api.Features.Requests;

/// <summary>
/// The shown state of requests: the stored lifecycle, and for an approved one how far the library got, read from the
/// library with Discover's availability rules (nothing stored, no scan). Processing while the version is wanted without a
/// file, partially available when some of the requested episodes have one, available when all have.
/// </summary>
public sealed class RequestStates(LibraryAvailability library)
{
    public async Task<Dictionary<int, RequestState>> ResolveAsync(IReadOnlyCollection<MediaRequest> requests, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var result = requests.Where(r => r.Status != RequestStatus.Approved).ToDictionary(r => r.Id, r => Stored(r.Status));
        var approved = requests.Where(r => r.Status == RequestStatus.Approved).ToList();
        if (approved.Count == 0)
        {
            return result;
        }

        var films = await library.FilmsAsync(approved.Where(r => r.MediaType == RequestMediaType.Movie).Select(r => r.TmdbId).Distinct().ToList(), ct);
        var series = await library.MatchSeriesAsync(approved.Where(r => r.MediaType == RequestMediaType.Series && r.SeriesId != null).Select(r => r.SeriesId!.Value).Distinct().ToList(), ct);
        foreach (var request in approved)
        {
            result[request.Id] = request.MediaType == RequestMediaType.Movie
                ? FromAvailability(films.GetValueOrDefault(request.TmdbId) is { } film ? Slot(film.Availability, request.FourK) : AvailabilityState.NotInLibrary)
                : OfSeasons(request, request.SeriesId is { } id ? series.GetValueOrDefault(id) : null);
        }

        return result;
    }

    public static RequestState Stored(RequestStatus status) => status switch
    {
        RequestStatus.Pending => RequestState.Pending,
        RequestStatus.Declined => RequestState.Declined,
        RequestStatus.Failed => RequestState.Failed,
        _ => RequestState.Approved,
    };

    private static AvailabilityState Slot(AvailabilityResponse availability, bool fourK) =>
        fourK ? availability.FourK ?? AvailabilityState.NotInLibrary : availability.Standard;

    private static RequestState FromAvailability(AvailabilityState state) => state switch
    {
        AvailabilityState.Available => RequestState.Available,
        AvailabilityState.PartiallyAvailable => RequestState.PartiallyAvailable,
        AvailabilityState.Processing => RequestState.Processing,
        _ => RequestState.Approved,
    };

    /// <summary>All requested seasons available: available; some episodes in: partial; wanted: processing.</summary>
    private static RequestState OfSeasons(MediaRequest request, SeriesMatch? match)
    {
        if (match is null || request.Seasons.Count == 0)
        {
            return RequestState.Approved;
        }

        var states = request.Seasons.Select(n => match.Seasons.GetValueOrDefault(n) is { } season ? Slot(season, request.FourK) : AvailabilityState.NotInLibrary).ToList();
        if (states.All(s => s == AvailabilityState.Available))
        {
            return RequestState.Available;
        }

        if (states.Any(s => s is AvailabilityState.Available or AvailabilityState.PartiallyAvailable))
        {
            return RequestState.PartiallyAvailable;
        }

        return states.Any(s => s == AvailabilityState.Processing) ? RequestState.Processing : RequestState.Approved;
    }
}
