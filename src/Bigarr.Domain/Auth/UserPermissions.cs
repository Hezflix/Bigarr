namespace Bigarr.Domain.Auth;

/// <summary>
/// The few things a user may or may not do beside their <see cref="UserRole"/>: a handful of switches, not a permission
/// mask. An admin has all of them whatever is stored (<see cref="User.Effective"/>).
/// </summary>
public sealed class UserPermissions
{
    public bool RequestFilms { get; set; }

    public bool RequestSeries { get; set; }

    /// <summary>May ask for the 4K version too, beside the standard one.</summary>
    public bool Request4K { get; set; }

    /// <summary>The user's film requests are approved as soon as they are made.</summary>
    public bool AutoApproveFilms { get; set; }

    /// <summary>The user's series requests are approved as soon as they are made.</summary>
    public bool AutoApproveSeries { get; set; }

    public bool ReportIssues { get; set; }

    /// <summary>What a new user gets until the admin changes the defaults: request films and series, report issues.</summary>
    public static UserPermissions Defaults() => new() { RequestFilms = true, RequestSeries = true, ReportIssues = true };

    public static UserPermissions All() => new()
    {
        RequestFilms = true,
        RequestSeries = true,
        Request4K = true,
        AutoApproveFilms = true,
        AutoApproveSeries = true,
        ReportIssues = true,
    };

    public UserPermissions Copy() => new()
    {
        RequestFilms = RequestFilms,
        RequestSeries = RequestSeries,
        Request4K = Request4K,
        AutoApproveFilms = AutoApproveFilms,
        AutoApproveSeries = AutoApproveSeries,
        ReportIssues = ReportIssues,
    };

    /// <summary>The names of the permissions that are on, as the session carries them.</summary>
    public IEnumerable<UserPermission> Granted() =>
        new (bool On, UserPermission Permission)[]
        {
            (RequestFilms, UserPermission.RequestFilms),
            (RequestSeries, UserPermission.RequestSeries),
            (Request4K, UserPermission.Request4K),
            (AutoApproveFilms, UserPermission.AutoApproveFilms),
            (AutoApproveSeries, UserPermission.AutoApproveSeries),
            (ReportIssues, UserPermission.ReportIssues),
        }.Where(p => p.On).Select(p => p.Permission);
}

/// <summary>One of <see cref="UserPermissions"/>' switches, by name: what an endpoint asks for.</summary>
public enum UserPermission
{
    RequestFilms,
    RequestSeries,
    Request4K,
    AutoApproveFilms,
    AutoApproveSeries,
    ReportIssues,
}
