using Bigarr.Domain.Auth;
using Bigarr.Domain.Discover;
using Bigarr.Domain.Events;
using Bigarr.Domain.Issues;
using Bigarr.Domain.Requests;

namespace Bigarr.Api.Features.Migration;

/// <summary>A Seerr user's permission bits as a Bigarr role and switches, with the bits Bigarr has nothing for.</summary>
public sealed record SeerrAccess(UserRole Role, UserPermissions Permissions, IReadOnlyList<string> Skipped);

/// <summary>
/// How Seerr's values read in Bigarr: its permission bits (<c>server/lib/permissions.ts</c>), request and issue statuses
/// (<c>server/constants</c>), Discover rows and Discord notification bits. Pure, for the preview and the import alike.
/// </summary>
public static class SeerrMapping
{
    // Seerr's Permission enum, only the bits read here.
    public const long Admin = 2;
    public const long ManageSettings = 4;
    public const long ManageUsers = 8;
    public const long ManageRequests = 16;
    public const long Request = 32;
    public const long Vote = 64;
    public const long AutoApprove = 128;
    public const long AutoApproveMovie = 256;
    public const long AutoApproveTv = 512;
    public const long Request4K = 1024;
    public const long Request4KMovie = 2048;
    public const long Request4KTv = 4096;
    public const long RequestAdvanced = 8192;
    public const long RequestView = 16384;
    public const long AutoApprove4K = 32768;
    public const long AutoApprove4KMovie = 65536;
    public const long AutoApprove4KTv = 131072;
    public const long RequestMovie = 262144;
    public const long RequestTv = 524288;
    public const long ManageIssues = 1048576;
    public const long ViewIssues = 2097152;
    public const long CreateIssues = 4194304;
    public const long AutoRequest = 8388608;
    public const long AutoRequestMovie = 16777216;
    public const long AutoRequestTv = 33554432;
    public const long RecentView = 67108864;
    public const long WatchlistView = 134217728;
    public const long ManageBlocklist = 268435456;
    public const long ViewBlocklist = 1073741824;

    /// <summary>The bits Bigarr has no switch for, by the name Seerr's settings give them.</summary>
    private static readonly (long Bit, string Name)[] Unmapped =
    [
        (ManageSettings, "Manage settings"),
        (ManageUsers, "Manage users"),
        (Vote, "Vote"),
        (RequestAdvanced, "Advanced requests"),
        (RequestView, "View requests"),
        (AutoApprove4K | AutoApprove4KMovie | AutoApprove4KTv, "Auto-approve 4K"),
        (ViewIssues, "View issues"),
        (AutoRequest | AutoRequestMovie | AutoRequestTv, "Auto-request (watchlist)"),
        (RecentView, "View recently added"),
        (WatchlistView, "View watchlists"),
        (ManageBlocklist | ViewBlocklist, "Blocklist"),
    ];

    /// <summary>
    /// Admin → admin. Manage requests or manage issues → request manager (Bigarr's one role for both). Request (both),
    /// request movie, request series → the two request switches; any 4K request bit → request 4K; auto-approve (both),
    /// movie, series → the two auto-approve switches; create issues → report issues. Everything else is skipped.
    /// </summary>
    public static SeerrAccess Access(long bits)
    {
        bool Has(long bit) => (bits & bit) != 0;

        if (Has(Admin))
        {
            return new SeerrAccess(UserRole.Admin, UserPermissions.All(), []);
        }

        var permissions = new UserPermissions
        {
            RequestFilms = Has(Request) || Has(RequestMovie),
            RequestSeries = Has(Request) || Has(RequestTv),
            Request4K = Has(Request4K) || Has(Request4KMovie) || Has(Request4KTv),
            AutoApproveFilms = Has(AutoApprove) || Has(AutoApproveMovie),
            AutoApproveSeries = Has(AutoApprove) || Has(AutoApproveTv),
            ReportIssues = Has(CreateIssues),
        };
        var role = Has(ManageRequests) || Has(ManageIssues) ? UserRole.RequestManager : UserRole.User;
        return new SeerrAccess(role, permissions, [.. Unmapped.Where(u => (bits & u.Bit) != 0).Select(u => u.Name)]);
    }

    /// <summary>
    /// Seerr's request status as Bigarr stores it. Completed (Seerr's "the title arrived") is approved: Bigarr reads
    /// available from the library. Null for a status Seerr does not have.
    /// </summary>
    public static RequestStatus? Status(int seerr) => seerr switch
    {
        1 => RequestStatus.Pending,
        2 or 5 => RequestStatus.Approved,
        3 => RequestStatus.Declined,
        4 => RequestStatus.Failed,
        _ => null,
    };

    public static string StatusName(int seerr) => seerr switch
    {
        1 => "pending",
        2 => "approved",
        3 => "declined",
        4 => "failed",
        5 => "completed",
        _ => $"unknown ({seerr})",
    };

    /// <summary>Seerr's <c>MediaStatus.AVAILABLE</c>.</summary>
    public const int MediaAvailable = 5;

    public static IssueType? IssueTypeOf(int seerr) => seerr is >= 1 and <= 4 ? (IssueType)seerr : null;

    public static IssueStatus IssueStatusOf(int seerr) => seerr == 2 ? IssueStatus.Resolved : IssueStatus.Open;

    /// <summary>A film's issue is about the film; a series' about the series (season 0), a season (episode 0) or an episode.</summary>
    public static IssueScope ScopeOf(bool movie, int season, int episode) =>
        movie ? IssueScope.Movie
        : season <= 0 ? IssueScope.Series
        : episode <= 0 ? IssueScope.Season
        : IssueScope.Episode;

    /// <summary>
    /// Seerr's Discord bits (<c>Notification</c>) as Bigarr's mention opt-ins. Seerr's "auto-approved" has no choice of
    /// its own in Bigarr: an auto-approved request posts as approved.
    /// </summary>
    public static List<string> Mentions(long bits)
    {
        var map = new (long Bit, Type Event)[]
        {
            (2, typeof(RequestCreated)),
            (4, typeof(RequestApproved)),
            (8, typeof(RequestAvailable)),
            (16, typeof(RequestFailed)),
            (64, typeof(RequestDeclined)),
            (256, typeof(IssueCreated)),
            (512, typeof(IssueCommented)),
            (1024, typeof(IssueResolved)),
            (2048, typeof(IssueReopened)),
        };
        var on = map.Where(m => (bits & m.Bit) != 0).Select(m => DomainEventCatalog.NameOf(m.Event)).ToHashSet(StringComparer.Ordinal);

        // Seerr has nothing like the new-episode messages: on by default, as for any user.
        on.Add(Domain.Notifications.Mentions.NewEpisode);
        return [.. Domain.Notifications.Mentions.Choices.Where(on.Contains)];
    }

    /// <summary>A Seerr Discover row as Bigarr's, or why it is skipped.</summary>
    public sealed record SliderMatch(DiscoverSliderKind? Kind, string? Value, string? Skipped);

    public static string SliderName(int type) => type switch
    {
        1 => "Recently added",
        2 => "Recent requests",
        3 => "Plex watchlist",
        4 => "Trending",
        5 => "Popular movies",
        6 => "Movie genres",
        7 => "Upcoming movies",
        8 => "Studios",
        9 => "Popular series",
        10 => "Series genres",
        11 => "Upcoming series",
        12 => "Networks",
        13 => "Movie keywords",
        14 => "Movie genre",
        15 => "Series keywords",
        16 => "Series genre",
        17 => "Search",
        18 => "Studio",
        19 => "Network",
        20 => "Movie streaming services",
        21 => "Series streaming services",
        _ => $"Unknown row ({type})",
    };

    /// <summary>Seerr's <c>DiscoverSliderType</c> as Bigarr's row kind; a custom row keeps its value when Bigarr can read it.</summary>
    public static SliderMatch Slider(int type, string? data)
    {
        var value = data?.Trim();
        return type switch
        {
            1 or 3 => new SliderMatch(null, null, "Reads the media server: Bigarr's Discover has no such row."),
            2 => new SliderMatch(null, null, "Bigarr shows requests on the Requests page, not on Discover."),
            4 => new SliderMatch(DiscoverSliderKind.Trending, null, null),
            5 => new SliderMatch(DiscoverSliderKind.PopularMovies, null, null),
            6 => new SliderMatch(DiscoverSliderKind.MovieGenres, null, null),
            7 => new SliderMatch(DiscoverSliderKind.UpcomingMovies, null, null),
            8 => new SliderMatch(DiscoverSliderKind.Studios, null, null),
            9 => new SliderMatch(DiscoverSliderKind.PopularSeries, null, null),
            10 => new SliderMatch(DiscoverSliderKind.SeriesGenres, null, null),
            11 => new SliderMatch(DiscoverSliderKind.UpcomingSeries, null, null),
            12 => new SliderMatch(DiscoverSliderKind.Networks, null, null),
            13 => OneId(DiscoverSliderKind.MovieKeyword, value),
            14 => OneId(DiscoverSliderKind.MovieGenre, value),
            15 => OneId(DiscoverSliderKind.SeriesKeyword, value),
            16 => OneId(DiscoverSliderKind.SeriesGenre, value),
            17 => new SliderMatch(null, null, "Seerr's search row mixes films and series; Bigarr's search rows are one or the other."),
            18 => OneId(DiscoverSliderKind.Studio, value),
            19 => OneId(DiscoverSliderKind.Network, value),
            20 or 21 => new SliderMatch(null, null, "Bigarr's Discover has no streaming service row."),
            _ => new SliderMatch(null, null, "Unknown row type."),
        };
    }

    private static SliderMatch OneId(DiscoverSliderKind kind, string? value) =>
        int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) && id > 0
            ? new SliderMatch(kind, id.ToString(System.Globalization.CultureInfo.InvariantCulture), null)
            : new SliderMatch(null, null, string.IsNullOrEmpty(value) ? "The row has no TMDB id." : $"Bigarr's row takes one TMDB id, Seerr's holds '{value}'.");
}
