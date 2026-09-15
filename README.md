# Bigarr

Unified media library manager: one application that replaces, brick by brick, Radarr, then Sonarr, Seerr, Tautulli and Prowlarr.

What Bigarr fixes at the model level: a movie can have **several versions** in the same instance (1080p, 4K, Director's Cut…), each monitored, searched and upgraded on its own. On the way out, Bigarr exposes a **Radarr v3 compatible API** so Seerr, Bazarr and the rest of the ecosystem plug in without noticing.

- Status: **MVP in progress**, milestone M3 (decision: quality profiles, custom formats, read-time scoring). Movies only.

## Stack

C# / .NET 10, ASP.NET Core minimal APIs, EF Core + SQLite, React + Vite + TanStack (tooled with Bun), multi-arch Docker image. GPL-3.0.

## Development

```bash
# backend — http://localhost:7979
dotnet build
dotnet test
dotnet run --project src/Bigarr.Api

# front — Vite dev server proxying /api to the backend
cd web && bun install && bun run dev

# production build of the front, served by the backend from wwwroot
cd web && bun run build
```

Metadata comes from TMDB. Set `Bigarr__Tmdb__ApiKey` (a v3 API key or a v4 read access token, free at themoviedb.org). Without it Bigarr runs on an embedded fixture catalogue of a dozen films, which is what the tests use.

Releases come from Torznab indexers, typically the ones a Prowlarr instance exposes. Set `Bigarr__Prowlarr__BaseUrl` and `Bigarr__Prowlarr__ApiKey` and Bigarr imports Prowlarr's torrent indexers at startup and on demand (Settings → Indexers → Sync now), pointing each at Prowlarr's per-indexer Torznab endpoint. Indexers can also be added by hand (URL such as `http://prowlarr:9696/1` plus the API key). If an SSO proxy sits in front of Prowlarr, exempt `/api` and `/<id>/api` from it: Prowlarr's own API key protects those paths. Without a tracker at hand, add an indexer of type **Mock**: it fabricates a realistic list of releases for any film, including a few that must be rejected, so the search screen can be exercised offline.

Quality profiles rank qualities and score **custom formats**. A custom format recognises a trait of a release (group, codec, HDR, language, size…) through conditions built by hand in Settings → Custom formats, where each profile also gets its score. The JSON shape is Radarr's, so Radarr exports and [TRaSH guide](https://trash-guides.info/Radarr/) files import unchanged (Import JSON, or `POST /api/v1/customformats/import`); the guide's suggested score is shown next to each profile and applied only on click. Every search, feed pass and file is judged with the formats from then on. Nothing is stored per release: editing a format or a score changes the next page.

The RSS loop polls every indexer's feed (every 15 minutes by default, `Bigarr__Rss__IntervalMinutes`, never below 10), matches each release to the library and judges it for every version; Activity → Releases shows the outcome. Automatic grabbing from the feed arrives with M6.

Downloads go through **qBittorrent** (WebUI API v2, 4.1 or later). Add it in Settings → Download clients with its host, port, credentials and the category Bigarr should own (created if missing); several clients are tried by priority. Grab a release from a film's interactive search: Bigarr fetches the `.torrent` (or passes the magnet), sends it with the category and records the grab, so the download is still followed after a restart. Activity → Queue is a live view of what the clients hold in that category (server-sent events; `Bigarr__Downloads__PollSeconds`, 10 by default): progress, ETA, the client's warnings, and a remove action that can also delete the files and blocklist the release. A torrent deleted by hand in qBittorrent leaves the queue after three polls and the version goes back to missing. Import into the library is the next milestone: a finished download waits in the queue as "import pending". Without a qBittorrent at hand, a download client of type **Mock** completes its downloads on its own after a minute and a half.

On first start an `admin` account is created. Set `Bigarr__InitialAdminPassword` to choose its password; otherwise a random one is generated and printed once in the logs.

## Deployment

A multi-arch image (amd64, arm64) is published to `ghcr.io/hezflix/bigarr` on every merge to `main`. See [`deploy/docker-compose.example.yml`](deploy/docker-compose.example.yml). Downloads and libraries must live under **one shared mount**, otherwise hardlinks are impossible and imports fall back to copies.

## License

GPL-3.0-or-later. The release parser is vendored from [Radarr](https://github.com/Radarr/Radarr) (GPL-3.0); see `src/Bigarr.Parsing/VENDORED.md` for the inventory and the upstream sync procedure.
