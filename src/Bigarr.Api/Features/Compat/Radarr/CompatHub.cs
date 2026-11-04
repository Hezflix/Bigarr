using System.Security.Claims;
using System.Text.Json;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Domain.Auth;
using Bigarr.Domain.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Bigarr.Api.Features.Compat.Radarr;

/// <summary>What Radarr sends over <c>receiveMessage</c>: a topic and a body; the body of a resource change carries the action and the resource.</summary>
public sealed record SignalRMessage(string Name, object Body);

public sealed record ResourceChange(string Action, ResourceStub Resource);

/// <summary>The part of the movie resource Bazarr reads from a change (§7.3): it fetches the rest with <c>GET /movie/{id}</c>.</summary>
public sealed record ResourceStub(int Id, string Title, int? Year, int TmdbId, string TitleSlug);

/// <summary>
/// Radarr's SignalR hub at <c>/signalr/messages</c> (§7.3): Bazarr relies on it for incremental updates. Connections are
/// grouped by the key's view, so a change reaches only the clients whose view contains the version.
/// </summary>
[Authorize(Policy = AuthPolicies.ApiKey)]
public sealed class CompatHub : Hub
{
    public const string Route = "/signalr/messages";

    public const string Method = "receiveMessage";

    public static string GroupFor(CompatView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return "view:" + view;
    }

    public override async Task OnConnectedAsync()
    {
        var view = Context.User?.CompatView() ?? CompatView.Default;
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(view));

        // Radarr greets with its version; Bazarr ignores the topic but the shape is what a Radarr client expects first.
        await Clients.Caller.SendAsync(Method, new SignalRMessage("version", new { version = RadarrCompat.ApplicationVersion }), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }
}

/// <summary>
/// Turns dispatched outbox events into hub messages (blueprint §3.9: the hub is a consumer of the outbox, like SSE and
/// webhooks). A version's change goes to the <c>all</c> group, to the <c>default</c> group when it is the default, and
/// to its <c>version:&lt;name&gt;</c> group; a film's deletion fans out over its versions.
/// </summary>
public sealed class CompatHubBroadcaster(EventStream stream, IHubContext<CompatHub> hub, ILogger<CompatHubBroadcaster> logger) : BackgroundService
{
    private static readonly Dictionary<string, Type> Types = DomainEventCatalog.Entries.ToDictionary(e => e.Type, e => e.Clr, StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var serverEvent in stream.SubscribeAsync(stoppingToken))
            {
                try
                {
                    foreach (var (view, message) in MessagesFor(serverEvent))
                    {
                        await hub.Clients.Group(view).SendAsync(CompatHub.Method, message, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not broadcast {Type} event {Id} to the compat hub", serverEvent.Type, serverEvent.Id);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    /// <summary>The (group, message) pairs one dispatched event produces. Pure, so the tests can check the routing without a hub.</summary>
    public static List<(string Group, SignalRMessage Message)> MessagesFor(ServerEvent serverEvent)
    {
        ArgumentNullException.ThrowIfNull(serverEvent);

        if (!Types.TryGetValue(serverEvent.Type, out var type))
        {
            return [];
        }

        var domainEvent = (DomainEvent?)JsonSerializer.Deserialize(serverEvent.Payload, type, EventJson.Options);
        return domainEvent switch
        {
            MovieAdded e => Change("created", e.Movie, e.Version),
            VersionAdded e => Change("created", e.Movie, e.Version),
            VersionUpdated e => Change("updated", e.Movie, e.Version),
            FileRenamed e => Change("updated", e.Movie, e.Version),
            FileImported e => Change("updated", e.Movie, e.Version),
            FileUpgraded e => Change("updated", e.Movie, e.Version),
            FileDeleted e => Change("updated", e.Movie, e.Version),
            VersionDeleted e => Change("deleted", e.Movie, e.Version),
            MovieDeleted e => (e.Versions ?? []).SelectMany(v => Change("deleted", e.Movie, v)).ToList(),
            DefaultVersionChanged e => DefaultSwitch(e),
            _ => [],
        };
    }

    private static List<(string, SignalRMessage)> Change(string action, MovieRef movie, VersionRef version)
    {
        var message = new SignalRMessage("movie", new ResourceChange(action, Stub(movie, version)));
        var groups = new List<(string, SignalRMessage)>
        {
            (CompatHub.GroupFor(CompatView.All), message),
            (CompatHub.GroupFor(CompatView.ForVersion(version.Name)), message),
        };
        if (version.IsDefault)
        {
            groups.Add((CompatHub.GroupFor(CompatView.Default), message));
        }

        return groups;
    }

    /// <summary>For a <c>default</c> key the film's line changes id: the old one goes away, the new one appears.</summary>
    private static List<(string, SignalRMessage)> DefaultSwitch(DefaultVersionChanged e)
    {
        var messages = new List<(string, SignalRMessage)>();
        if (e.PreviousDefault is { } previous)
        {
            messages.Add((CompatHub.GroupFor(CompatView.Default), new SignalRMessage("movie", new ResourceChange("deleted", Stub(e.Movie, previous)))));
        }

        messages.Add((CompatHub.GroupFor(CompatView.Default), new SignalRMessage("movie", new ResourceChange("updated", Stub(e.Movie, e.NewDefault)))));
        return messages;
    }

    private static ResourceStub Stub(MovieRef movie, VersionRef version) =>
        new(version.Id, movie.Title, movie.Year, movie.TmdbId, movie.TmdbId.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
