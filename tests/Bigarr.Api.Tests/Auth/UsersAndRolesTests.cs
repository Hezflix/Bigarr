using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Auth;
using Bigarr.Api.Features.Users;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Compat;
using Bigarr.Api.Tests.Playback;
using Bigarr.Domain.Auth;
using Bigarr.Domain.MediaServers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Bigarr.Api.Tests.Auth;

/// <summary>
/// Other people sign in with Plex when they have access to the Plex server set up in Bigarr, get the default
/// permissions on their first sign-in, and reach nothing of Bigarr's own pages and API; the admin changes their role
/// and permissions and disables them from Settings → Users. All against a fake plex.tv and a fake Plex server.
/// </summary>
public sealed class UsersAndRolesTests
{
    [Fact]
    public async Task A_plex_account_with_access_to_the_server_signs_in_and_gets_the_default_permissions()
    {
        await using var env = await Env.StartAsync();
        await env.OpenToNewAccountsAsync(new PermissionsDto(RequestFilms: true, RequestSeries: false, Request4K: true, AutoApproveFilms: false, AutoApproveSeries: false, ReportIssues: true));
        env.PlexTv.Share(FakePlexTvSignIn.Friend, "some-other-server", FakePlex.MachineIdentifier);

        // Open to new accounts: the login page offers Plex although no one is linked.
        Assert.Single(await env.Factory.CreateAnonymousClient().GetFromJsonAsync<JsonElement[]>("/api/v1/auth/providers") ?? []);

        var browser = env.Factory.CreateClientWithCookies();
        Assert.Equal("signedIn", await env.SignInAsync(browser, FakePlexTvSignIn.Friend));

        var me = await browser.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        Assert.Equal("demo-friend", me!.Username);
        Assert.Equal("User", me.Role);
        Assert.Equal(["RequestFilms", "Request4K", "ReportIssues"], me.Permissions);

        using (var scope = env.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var user = db.Users.Single(u => u.Username == "demo-friend");
            Assert.Equal(UserRole.User, user.Role);
            Assert.False(user.HasPassword);
            Assert.NotNull(user.LastLoginAt);
            var link = db.ExternalLogins.Single(l => l.UserId == user.Id);
            Assert.Equal("3003", link.ExternalId);
            Assert.Equal("friend@example.invalid", link.Email);
        }

        // The check used the friend's own token, sent to plex.tv only.
        Assert.Contains(env.PlexTv.Requests, r => r.Path == "/api/v2/resources" && r.Headers.ContainsKey("X-Plex-Token"));
        Assert.DoesNotContain(env.Plex.Requests, r => r.Headers.TryGetValue("X-Plex-Token", out var token) && token.StartsWith("fake-token-", StringComparison.Ordinal));

        // Nothing of Bigarr's own: pages, settings, keys, the compatible APIs.
        foreach (var path in new[] { "/api/v1/system/status", "/api/v1/movies", "/api/v1/users", "/api/v1/apikeys", "/api/v1/auth/external", "/api/v1/calendar/bigarr.ics" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await browser.GetAsync(path)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostAsJsonAsync("/api/v1/apikeys", new { name = "mine" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/radarr/api/v3/system/status")).StatusCode);

        // A second sign-in finds the same user.
        var again = env.Factory.CreateClientWithCookies();
        Assert.Equal("signedIn", await env.SignInAsync(again, FakePlexTvSignIn.Friend));
        Assert.Equal(me.Id, (await again.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!.Id);
        Assert.Equal(HttpStatusCode.OK, (await browser.PostAsync("/api/v1/auth/logout", content: null)).StatusCode);
    }

    [Fact]
    public async Task A_plex_account_without_access_is_refused_and_no_user_is_created()
    {
        await using var env = await Env.StartAsync();
        await env.OpenToNewAccountsAsync(PermissionsDto.From(UserPermissions.Defaults()));
        env.PlexTv.Share(FakePlexTvSignIn.Stranger, "some-other-server");

        var browser = env.Factory.CreateClientWithCookies();
        Assert.Equal("noServerAccess", await env.SignInAsync(browser, FakePlexTvSignIn.Stranger));
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Single(await env.UsersAsync()); // the admin only
    }

    [Fact]
    public async Task New_accounts_are_let_in_by_default_and_stay_out_once_the_admin_turns_it_off()
    {
        await using var env = await Env.StartAsync();
        var settings = await env.Admin.GetFromJsonAsync<UserSettingsResponse>("/api/v1/users/settings");
        Assert.True(settings!.PlexNewAccounts);
        Assert.True(settings.PlexServerConfigured);
        Assert.Equal(PermissionsDto.From(UserPermissions.Defaults()), settings.DefaultPermissions);

        // Plex on: offered on the login page although no one is linked yet.
        await env.EnablePlexAsync();
        Assert.Single(await env.Factory.CreateAnonymousClient().GetFromJsonAsync<JsonElement[]>("/api/v1/auth/providers") ?? []);

        var off = await env.Admin.PutAsJsonAsync("/api/v1/users/settings", new UpdateUserSettingsRequest(false, settings.DefaultPermissions));
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);

        // Off and linked to no one: nothing offered; once the admin is linked, a friend with access is still refused.
        Assert.Empty(await env.Factory.CreateAnonymousClient().GetFromJsonAsync<JsonElement[]>("/api/v1/auth/providers") ?? []);
        env.PlexTv.Share(FakePlexTvSignIn.Friend, FakePlex.MachineIdentifier);
        await env.LinkAdminAsync();
        Assert.Equal("unknownAccount", await env.SignInAsync(env.Factory.CreateClientWithCookies(), FakePlexTvSignIn.Friend));
        Assert.Single(await env.UsersAsync());
    }

    [Fact]
    public async Task When_the_server_cannot_be_asked_a_new_account_is_refused_but_the_admin_still_signs_in()
    {
        await using var env = await Env.StartAsync(plexUrl: "http://127.0.0.1:9");
        await env.OpenToNewAccountsAsync(PermissionsDto.From(UserPermissions.Defaults()));
        await env.LinkAdminAsync();
        env.PlexTv.Share(FakePlexTvSignIn.Friend, FakePlex.MachineIdentifier);

        Assert.Equal("accessUnknown", await env.SignInAsync(env.Factory.CreateClientWithCookies(), FakePlexTvSignIn.Friend));

        // The admin's Plex account has no access to anything here, and the server is down: the admin signs in anyway.
        var admin = env.Factory.CreateClientWithCookies();
        Assert.Equal("signedIn", await env.SignInAsync(admin, FakePlexTvSignIn.Owner));
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/system/status")).StatusCode);
    }

    [Fact]
    public async Task A_user_removed_from_the_server_no_longer_signs_in_and_the_password_login_never_opens_for_them()
    {
        await using var env = await Env.StartAsync();
        await env.OpenToNewAccountsAsync(PermissionsDto.From(UserPermissions.Defaults()));
        env.PlexTv.Share(FakePlexTvSignIn.Friend, FakePlex.MachineIdentifier);
        Assert.Equal("signedIn", await env.SignInAsync(env.Factory.CreateClientWithCookies(), FakePlexTvSignIn.Friend));

        env.PlexTv.ResourcesDown = true;
        Assert.Equal("accessUnknown", await env.SignInAsync(env.Factory.CreateClientWithCookies(), FakePlexTvSignIn.Friend));
        env.PlexTv.ResourcesDown = false;

        env.PlexTv.Share(FakePlexTvSignIn.Friend);
        Assert.Equal("noServerAccess", await env.SignInAsync(env.Factory.CreateClientWithCookies(), FakePlexTvSignIn.Friend));

        // A user created by Plex has no password: no password opens their account.
        foreach (var password in new[] { string.Empty, " ", "anything" })
        {
            var local = await env.Factory.CreateClientWithCookies().PostAsJsonAsync("/api/v1/auth/login", new { username = "demo-friend", password });
            Assert.True(local.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest, $"{password}: {local.StatusCode}");
        }
    }

    [Fact]
    public async Task The_admin_changes_a_users_role_and_permissions_and_the_open_session_follows()
    {
        await using var env = await Env.StartAsync();
        await env.OpenToNewAccountsAsync(PermissionsDto.From(UserPermissions.Defaults()));
        env.PlexTv.Share(FakePlexTvSignIn.Friend, FakePlex.MachineIdentifier);
        var friend = env.Factory.CreateClientWithCookies();
        Assert.Equal("signedIn", await env.SignInAsync(friend, FakePlexTvSignIn.Friend));

        var listed = Assert.Single(await env.UsersAsync(), u => u.Username == "demo-friend");
        Assert.Equal(UserRole.User, listed.Role);
        Assert.False(listed.HasPassword);
        Assert.False(listed.IsYou);
        var plex = Assert.Single(listed.Logins);
        Assert.Equal(("plex", "demo-friend", "friend@example.invalid"), (plex.Provider, plex.ExternalUsername, plex.Email));
        Assert.True(Assert.Single(await env.UsersAsync(), u => u.Username == BigarrApiFactory.AdminUsername).IsYou);

        var update = await env.Admin.PutAsJsonAsync($"/api/v1/users/{listed.Id}", new
        {
            role = "requestManager",
            permissions = new PermissionsDto(RequestFilms: true, RequestSeries: true, Request4K: true, AutoApproveFilms: true, AutoApproveSeries: false, ReportIssues: false),
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<UserResponse>(CompatTestHelpers.Json);
        Assert.Equal(UserRole.RequestManager, updated!.Role);

        // The friend's session, opened before, carries the new role and permissions on its next request.
        var me = await friend.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        Assert.Equal("RequestManager", me!.Role);
        Assert.Equal(["RequestFilms", "RequestSeries", "Request4K", "AutoApproveFilms"], me.Permissions);
        Assert.Equal(HttpStatusCode.Forbidden, (await friend.GetAsync("/api/v1/system/status")).StatusCode);

        // Made an admin: Bigarr's pages open, every permission with them.
        await env.Admin.PutAsJsonAsync($"/api/v1/users/{listed.Id}", new { role = "admin", permissions = updated.Permissions });
        Assert.Equal(HttpStatusCode.OK, (await friend.GetAsync("/api/v1/system/status")).StatusCode);
        Assert.Equal(6, (await friend.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!.Permissions.Count);

        // The admin cannot take their own role away.
        var self = await env.Admin.PutAsJsonAsync($"/api/v1/users/{env.AdminId}", new { role = "user", permissions = updated.Permissions });
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await env.Admin.PutAsJsonAsync("/api/v1/users/999", new { role = "user", permissions = updated.Permissions })).StatusCode);
    }

    [Fact]
    public async Task A_disabled_user_loses_their_sessions_keys_and_sign_ins_until_enabled_again()
    {
        await using var env = await Env.StartAsync();
        await env.OpenToNewAccountsAsync(PermissionsDto.From(UserPermissions.Defaults()));
        env.PlexTv.Share(FakePlexTvSignIn.Friend, FakePlex.MachineIdentifier);
        var friend = env.Factory.CreateClientWithCookies();
        Assert.Equal("signedIn", await env.SignInAsync(friend, FakePlexTvSignIn.Friend));
        var id = (await friend.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!.Id;

        // Made an admin for a while, the friend creates an API key.
        await env.Admin.PutAsJsonAsync($"/api/v1/users/{id}", new { role = "admin", permissions = PermissionsDto.From(UserPermissions.Defaults()) });
        var (keyClient, _) = await CompatTestHelpers.KeyClientAsync(env.Factory, friend, "friend-script", "default", header: true);
        Assert.Equal(HttpStatusCode.OK, (await keyClient.GetAsync("/radarr/api/v3/system/status")).StatusCode);

        var disabled = await env.Admin.PostAsync($"/api/v1/users/{id}/disable", content: null);
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        Assert.NotNull((await disabled.Content.ReadFromJsonAsync<UserResponse>(CompatTestHelpers.Json))!.DisabledAt);

        Assert.Equal(HttpStatusCode.Unauthorized, (await friend.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await keyClient.GetAsync("/radarr/api/v3/system/status")).StatusCode);
        Assert.Equal("userDisabled", await env.SignInAsync(env.Factory.CreateClientWithCookies(), FakePlexTvSignIn.Friend));

        // The admin cannot disable themselves; enabling the friend lets them back in.
        Assert.Equal(HttpStatusCode.Conflict, (await env.Admin.PostAsync($"/api/v1/users/{env.AdminId}/disable", content: null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await env.Admin.PostAsync($"/api/v1/users/{id}/enable", content: null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await keyClient.GetAsync("/radarr/api/v3/system/status")).StatusCode);
        var back = env.Factory.CreateClientWithCookies();
        Assert.Equal("signedIn", await env.SignInAsync(back, FakePlexTvSignIn.Friend));
        Assert.Equal(HttpStatusCode.OK, (await back.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task A_demoted_admin_s_key_no_longer_opens_the_compat_surfaces()
    {
        await using var env = await Env.StartAsync();
        await env.OpenToNewAccountsAsync(PermissionsDto.From(UserPermissions.Defaults()));
        env.PlexTv.Share(FakePlexTvSignIn.Friend, FakePlex.MachineIdentifier);
        var friend = env.Factory.CreateClientWithCookies();
        Assert.Equal("signedIn", await env.SignInAsync(friend, FakePlexTvSignIn.Friend));
        var id = (await friend.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!.Id;
        var permissions = PermissionsDto.From(UserPermissions.Defaults());

        // Made an admin for a while, the friend creates an API key that opens both compat surfaces.
        await env.Admin.PutAsJsonAsync($"/api/v1/users/{id}", new { role = "admin", permissions });
        var (keyClient, _) = await CompatTestHelpers.KeyClientAsync(env.Factory, friend, "friend-script", "default", header: true);
        Assert.Equal(HttpStatusCode.OK, (await keyClient.GetAsync("/radarr/api/v3/system/status")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await keyClient.GetAsync("/sonarr/api/v3/system/status")).StatusCode);

        // Back to an ordinary user: the key is still valid but no longer allowed on those surfaces.
        await env.Admin.PutAsJsonAsync($"/api/v1/users/{id}", new { role = "user", permissions });
        Assert.Equal(HttpStatusCode.Forbidden, (await keyClient.GetAsync("/radarr/api/v3/system/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await keyClient.GetAsync("/sonarr/api/v3/system/status")).StatusCode);

        // Promoted again, the same key works again.
        await env.Admin.PutAsJsonAsync($"/api/v1/users/{id}", new { role = "admin", permissions });
        Assert.Equal(HttpStatusCode.OK, (await keyClient.GetAsync("/radarr/api/v3/system/status")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await keyClient.GetAsync("/sonarr/api/v3/system/status")).StatusCode);
    }

    [Fact]
    public async Task A_demoted_owner_s_full_key_opens_nothing_admin_until_promoted_again()
    {
        await using var env = await Env.StartAsync();
        await env.OpenToNewAccountsAsync(PermissionsDto.From(UserPermissions.Defaults()));
        env.PlexTv.Share(FakePlexTvSignIn.Friend, FakePlex.MachineIdentifier);
        var friend = env.Factory.CreateClientWithCookies();
        Assert.Equal("signedIn", await env.SignInAsync(friend, FakePlexTvSignIn.Friend));
        var id = (await friend.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!.Id;
        var permissions = PermissionsDto.From(UserPermissions.Defaults());

        // Made an admin for a while, the friend creates a key of scope full for a script.
        await env.Admin.PutAsJsonAsync($"/api/v1/users/{id}", new { role = "admin", permissions });
        var created = await friend.PostAsJsonAsync("/api/v1/apikeys", new { name = "friend-script", scope = "full" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var script = env.Factory.CreateAnonymousClient();
        script.DefaultRequestHeaders.Add("X-Api-Key", (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("key").GetString()!);
        Assert.Equal(HttpStatusCode.OK, (await script.GetAsync("/api/v1/movies")).StatusCode);

        // Back to an ordinary user: the scope still says full, but the role no longer allows the admin API.
        await env.Admin.PutAsJsonAsync($"/api/v1/users/{id}", new { role = "user", permissions });
        Assert.Equal(HttpStatusCode.Forbidden, (await script.GetAsync("/api/v1/movies")).StatusCode);

        // Promoted again, the same key works again.
        await env.Admin.PutAsJsonAsync($"/api/v1/users/{id}", new { role = "admin", permissions });
        Assert.Equal(HttpStatusCode.OK, (await script.GetAsync("/api/v1/movies")).StatusCode);
    }

    [Fact]
    public async Task A_disabled_local_user_cannot_sign_in_with_the_password()
    {
        await using var env = await Env.StartAsync();
        int id;
        using (var scope = env.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.IPasswordHasher<User>>();
            var user = new User { Username = "demo-local", PasswordHash = string.Empty, Role = UserRole.User };
            user.PasswordHash = hasher.HashPassword(user, "demo-password-1");
            db.Users.Add(user);
            await db.SaveChangesAsync();
            id = user.Id;
        }

        var login = new LoginRequest("demo-local", "demo-password-1");
        var client = env.Factory.CreateClientWithCookies();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/login", login)).StatusCode);
        Assert.Equal("User", (await client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!.Role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/auth/password", new ChangePasswordRequest("demo-password-1", "another-password"))).StatusCode);

        await env.Admin.PostAsync($"/api/v1/users/{id}/disable", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await env.Factory.CreateClientWithCookies().PostAsJsonAsync("/api/v1/auth/login", login)).StatusCode);
    }

    [Fact]
    public async Task Policies_give_each_role_and_permission_what_it_names_and_nothing_else()
    {
        await using var factory = new BigarrApiFactory();
        var authorization = factory.Services.GetRequiredService<IAuthorizationService>();

        var admin = Principal(UserRole.Admin, new UserPermissions());
        var manager = Principal(UserRole.RequestManager, new UserPermissions { RequestFilms = true });
        var user = Principal(UserRole.User, new UserPermissions { RequestFilms = true, ReportIssues = true });

        async Task<bool> Allowed(System.Security.Claims.ClaimsPrincipal who, string policy) => (await authorization.AuthorizeAsync(who, policy)).Succeeded;

        foreach (var policy in new[] { AuthPolicies.Session, AuthPolicies.Api })
        {
            Assert.True(await Allowed(admin, policy));
            Assert.False(await Allowed(manager, policy));
            Assert.False(await Allowed(user, policy));
        }

        Assert.True(await Allowed(admin, AuthPolicies.ManageRequests));
        Assert.True(await Allowed(manager, AuthPolicies.ManageRequests));
        Assert.False(await Allowed(user, AuthPolicies.ManageRequests));
        Assert.True(await Allowed(user, AuthPolicies.SignedIn));

        foreach (var permission in Enum.GetValues<UserPermission>())
        {
            Assert.True(await Allowed(admin, AuthPolicies.Has(permission)), $"admin {permission}");
            Assert.Equal(permission == UserPermission.RequestFilms, await Allowed(manager, AuthPolicies.Has(permission)));
            Assert.Equal(permission is UserPermission.RequestFilms or UserPermission.ReportIssues, await Allowed(user, AuthPolicies.Has(permission)));
        }

        static System.Security.Claims.ClaimsPrincipal Principal(UserRole role, UserPermissions permissions) =>
            BigarrPrincipal.ForUser(new User { Id = 7, Username = "demo-" + role, PasswordHash = "x", Role = role, Permissions = permissions }, AuthSchemes.Session);
    }

    [Fact]
    public async Task Every_endpoint_is_the_admins_unless_opened_on_purpose()
    {
        await using var factory = new BigarrApiFactory();
        _ = factory.CreateClient(); // builds the app
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();

        // What other people reach, by name: the login page and its flows, who-am-I, sign out, and Discover (TMDB's
        // catalogue with availability states only; its row settings stay the admin's), requests, and their own
        // notification settings.
        string[] open =
        [
            "GET /api/v1/auth/me",
            "POST /api/v1/auth/logout",
            "GET /api/v1/profile/notifications",
            "PUT /api/v1/profile/notifications",

            // Their own shows (what they watch) and the switch that stops one show's new-episode messages.
            "GET /api/v1/profile/shows",
            "PUT /api/v1/profile/shows/{seriesId:int}",
            "GET /api/v1/discover/",
            "GET /api/v1/discover/sliders/{id:int}",
            "GET /api/v1/discover/movies",
            "GET /api/v1/discover/series",
            "GET /api/v1/discover/search",
            "GET /api/v1/discover/movies/{tmdbId:int}",
            "GET /api/v1/discover/series/{tmdbId:int}",
            "GET /api/v1/discover/people/{tmdbId:int}",

            // Requests: everyone lists their own, asks and cancels; approvers (admin, request manager) decide.
            "GET /api/v1/requests/",
            "POST /api/v1/requests/",
            "GET /api/v1/requests/quota",
            "GET /api/v1/requests/{id:int}",
            "DELETE /api/v1/requests/{id:int}",
            "GET /api/v1/requests/count",
            "GET /api/v1/requests/options",
            "POST /api/v1/requests/{id:int}/approve",
            "POST /api/v1/requests/{id:int}/decline",
            "POST /api/v1/requests/{id:int}/retry",

            // Issues: the reporter and approvers read, comment, resolve and reopen; "report issues" reports; approvers act.
            "GET /api/v1/issues/",
            "GET /api/v1/issues/title",
            "GET /api/v1/issues/{id:int}",
            "POST /api/v1/issues/{id:int}/comments",
            "PUT /api/v1/issues/{id:int}/comments/{commentId:int}",
            "DELETE /api/v1/issues/{id:int}/comments/{commentId:int}",
            "POST /api/v1/issues/{id:int}/resolve",
            "POST /api/v1/issues/{id:int}/reopen",
            "POST /api/v1/issues",
            "GET /api/v1/issues/count",
            "POST /api/v1/issues/{id:int}/search",
            "POST /api/v1/issues/{id:int}/blocklist",
            "POST /api/v1/issues/{id:int}/delete-file",
        ];
        string[] adminPolicies = [AuthPolicies.Session, AuthPolicies.Api, AuthPolicies.ConnectedApp, AuthPolicies.CalendarFeed];
        var opened = new List<string>();
        foreach (var endpoint in endpoints)
        {
            var name = $"{string.Join(',', endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? ["*"])} /{endpoint.RoutePattern.RawText?.TrimStart('/')}";
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            {
                continue;
            }

            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).ToList();
            if (policies.Count == 0 || policies.All(p => p is null || adminPolicies.Contains(p) || p == AuthPolicies.ApiKey))
            {
                continue; // the fallback (admin), an admin policy (a key opens those only for an admin owner, within its scope), or the API key surfaces
            }

            opened.Add(name);
        }

        Assert.Equal(open.Order(), opened.Order());
    }

    /// <summary>A Bigarr with a fake plex.tv, a fake Plex server set up in Media servers, and the admin's session.</summary>
    private sealed class Env : IAsyncDisposable
    {
        private Env(FakePlexTvSignIn plexTv, FakePlex plex, BigarrApiFactory factory, HttpClient admin, int adminId)
        {
            PlexTv = plexTv;
            Plex = plex;
            Factory = factory;
            Admin = admin;
            AdminId = adminId;
        }

        public FakePlexTvSignIn PlexTv { get; }

        public FakePlex Plex { get; }

        public BigarrApiFactory Factory { get; }

        public HttpClient Admin { get; }

        public int AdminId { get; }

        public static async Task<Env> StartAsync(string? plexUrl = null)
        {
            var plexTv = await FakePlexTvSignIn.StartAsync();
            var plex = await FakePlex.StartAsync();
            var factory = new ConfiguredBigarrApiFactory(null, new Dictionary<string, string> { ["Bigarr:PlexTv:BaseUrl"] = plexTv.Url });
            var admin = await CompatTestHelpers.SessionAsync(factory);
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
                db.MediaServers.Add(new MediaServer { Name = "Demo Plex", Url = plexUrl ?? plex.Url, Token = FakePlex.Token });
                await db.SaveChangesAsync();
            }

            var me = await admin.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
            return new Env(plexTv, plex, factory, admin, me!.Id);
        }

        public async Task EnablePlexAsync() =>
            Assert.Equal(HttpStatusCode.NoContent, (await Admin.PutAsJsonAsync("/api/v1/auth/external/plex", new { enabled = true })).StatusCode);

        public async Task OpenToNewAccountsAsync(PermissionsDto defaults)
        {
            await EnablePlexAsync();
            var response = await Admin.PutAsJsonAsync("/api/v1/users/settings", new UpdateUserSettingsRequest(true, defaults));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        public async Task LinkAdminAsync()
        {
            var link = await StartFlowAsync(Admin, "/api/v1/auth/external/plex/link");
            PlexTv.Approve(PlexTv.LastPinId, FakePlexTvSignIn.Owner);
            Assert.Equal("linked", await PollAsync(Admin, link));
        }

        public async Task<string> SignInAsync(HttpClient browser, FakePlexTvSignIn.Account account)
        {
            var flow = await StartFlowAsync(browser, "/api/v1/auth/external/plex/login");
            PlexTv.Approve(PlexTv.LastPinId, account);
            return await PollAsync(browser, flow);
        }

        public async Task<List<UserResponse>> UsersAsync() =>
            await Admin.GetFromJsonAsync<List<UserResponse>>("/api/v1/users", CompatTestHelpers.Json) ?? [];

        private static async Task<string> StartFlowAsync(HttpClient client, string path)
        {
            var response = await client.PostAsJsonAsync(path, new { });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("flowId").GetString()!;
        }

        private static async Task<string> PollAsync(HttpClient client, string flowId)
        {
            var response = await client.PostAsJsonAsync("/api/v1/auth/external/poll", new { flowId });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString()!;
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            await Plex.DisposeAsync();
            await PlexTv.DisposeAsync();
        }
    }
}
