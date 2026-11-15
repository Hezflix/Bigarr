namespace Bigarr.Domain.Auth;

/// <summary>
/// What a user is, coarse on purpose: the admin runs Bigarr, a request manager handles other people's requests and
/// issues, a user asks for things. What a user or a request manager may ask for is in <see cref="UserPermissions"/>.
/// </summary>
public enum UserRole
{
    /// <summary>Everything, every permission included. The only role that reaches Bigarr's own pages and API.</summary>
    Admin = 1,

    /// <summary>Approves or declines other people's requests and handles issues; what they ask for themselves follows their permissions.</summary>
    RequestManager = 2,

    /// <summary>Signs in to ask for films and series and to report issues, as their permissions allow.</summary>
    User = 3,
}
