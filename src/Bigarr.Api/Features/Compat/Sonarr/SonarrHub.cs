using System.Text.Json;
using Bigarr.Api.Features.Compat.Radarr;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Domain.Auth;
using Bigarr.Domain.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Bigarr.Api.Features.Compat.Sonarr;

/// <summary>A series change as Bazarr reads it: <c>id</c>, <c>title</c> and <c>year</c> always there, <c>episodesChanged</c> when monitoring moved.</summary>
public sealed record SonarrSeriesStub(int Id, string Title, int Year, int TvdbId, string TitleSlug, bool EpisodesChanged);

/// <summary>The show an episode change belongs to.</summary>
public sealed record SonarrEpisodeSeries(int Id, string Title, int Year, int TvdbId);

/// <summary>An episode change: Bazarr needs <c>id</c>, <c>title</c>, the numbers and the show (<c>seriesId</c> or <c>series</c>), then reads the rest itself.</summary>
public sealed record SonarrEpisodeStub(int Id, int SeriesId, string Title, int SeasonNumber, int EpisodeNumber, int? AbsoluteEpisodeNumber, SonarrEpisodeSeries Series);

public sealed record SonarrChange(string Action, object Resource);

/// <summary>
/// Sonarr's SignalR hub at <c>{base}/signalr/messages</c>: Bazarr follows series and episodes through it. Apart from the
/// Radarr hub so a Bazarr set up for Sonarr never hears about films; connections are grouped by the key's view.
/// </summary>
[Authorize(Policy = AuthPolicies.ApiKey)]
public sealed class SonarrHub : Hub
{
    public const string Route = SonarrCompat.UrlBase + "/signalr/messages";

    public static string GroupFor(CompatView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return "sonarr:view:" + view;
    }

    public override async Task OnConnectedAsync()
    {
        var view = Context.User?.CompatView() ?? CompatView.Default;
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(view));
        await Clients.Caller.SendAsync(CompatHub.Method, new SignalRMessage("version", new { version = SonarrCompat.ApplicationVersion }), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }
}

/// <summary>
/// Outbox events into Sonarr messages, routed by version like the Radarr hub: a file imported, upgraded or deleted is an
/// <c>episode updated</c> per episode (Bazarr syncs episodes from those); a monitoring change is <c>series updated</c>
/// with <c>episodesChanged</c>; a rename too, as it moves every file path Bazarr knows; a version or show deleted is
/// <c>series deleted</c>.
/// </summary>
public sealed class SonarrHubBroadcaster(EventStream stream, IHubContext<SonarrHub> hub, ILogger<SonarrHubBroadcaster> logger) : BackgroundService
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
                    foreach (var (group, message) in MessagesFor(serverEvent))
                    {
                        await hub.Clients.Group(group).SendAsync(CompatHub.Method, message, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not broadcast {Type} event {Id} to the Sonarr hub", serverEvent.Type, serverEvent.Id);
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
            SeriesAdded e => Series("updated", e.Series, e.Version),
            SeriesVersionAdded e => Series("updated", e.Series, e.Version),
            SeriesVersionUpdated e => Series("updated", e.Series, e.Version, e.EpisodesChanged),
            EpisodeFileRenamed e => Series("updated", e.Series, e.Version, episodesChanged: true),
            SeriesVersionDeleted e => Series("deleted", e.Series, e.Version),
            SeriesDeleted e => (e.Versions ?? []).SelectMany(v => Series("deleted", e.Series, v)).ToList(),
            SeriesDefaultVersionChanged e => DefaultSwitch(e),
            EpisodeFileImported e => Episodes(e.Series, e.Version, e.Episodes),
            EpisodeFileDeleted e => Episodes(e.Series, e.Version, e.Episodes),
            _ => [],
        };
    }

    private static List<(string, SignalRMessage)> Series(string action, SeriesRef series, SeriesVersionRef version, bool episodesChanged = false) =>
        Route(version, new SignalRMessage("series", new SonarrChange(action, Stub(series, version, episodesChanged))));

    private static List<(string, SignalRMessage)> Episodes(SeriesRef series, SeriesVersionRef version, List<EpisodeRef> episodes) =>
        episodes.SelectMany(e => Route(version, new SignalRMessage("episode", new SonarrChange("updated", new SonarrEpisodeStub(
            e.Id,
            version.Id,
            e.Title,
            e.SeasonNumber,
            e.EpisodeNumber,
            e.AbsoluteEpisodeNumber,
            new SonarrEpisodeSeries(version.Id, series.Title, series.Year ?? 0, series.TvdbId ?? 0)))))).ToList();

    private static List<(string, SignalRMessage)> Route(SeriesVersionRef version, SignalRMessage message)
    {
        var groups = new List<(string, SignalRMessage)>
        {
            (SonarrHub.GroupFor(CompatView.All), message),
            (SonarrHub.GroupFor(CompatView.ForVersion(version.Name)), message),
        };
        if (version.IsDefault)
        {
            groups.Add((SonarrHub.GroupFor(CompatView.Default), message));
        }

        return groups;
    }

    /// <summary>For a <c>default</c> key the show's line changes id: the old one goes, the new one comes with its episodes.</summary>
    private static List<(string, SignalRMessage)> DefaultSwitch(SeriesDefaultVersionChanged e)
    {
        var group = SonarrHub.GroupFor(CompatView.Default);
        var messages = new List<(string, SignalRMessage)>();
        if (e.PreviousDefault is { } previous)
        {
            messages.Add((group, new SignalRMessage("series", new SonarrChange("deleted", Stub(e.Series, previous, false)))));
        }

        messages.Add((group, new SignalRMessage("series", new SonarrChange("updated", Stub(e.Series, e.NewDefault, true)))));
        return messages;
    }

    private static SonarrSeriesStub Stub(SeriesRef series, SeriesVersionRef version, bool episodesChanged) =>
        new(version.Id, series.Title, series.Year ?? 0, series.TvdbId ?? 0, (series.TvdbId ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture), episodesChanged);
}
