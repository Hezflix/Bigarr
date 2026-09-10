# Bigarr

Unified media library manager: one application that replaces, brick by brick, Radarr, then Sonarr, Seerr, Tautulli and Prowlarr.

What Bigarr fixes at the model level: a movie can have **several versions** in the same instance (1080p, 4K, Director's Cut…), each monitored, searched and upgraded on its own. On the way out, Bigarr exposes a **Radarr v3 compatible API** so Seerr, Bazarr and the rest of the ecosystem plug in without noticing.

- Status: **MVP in progress**, milestone M2 (search: Torznab indexers, interactive search with explicit rejection reasons). Movies only.

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

The RSS loop polls every indexer's feed (every 15 minutes by default, `Bigarr__Rss__IntervalMinutes`, never below 10), matches each release to the library and judges it for every version; Activity → Releases shows the outcome. Grabbing arrives with the download client.

On first start an `admin` account is created. Set `Bigarr__InitialAdminPassword` to choose its password; otherwise a random one is generated and printed once in the logs.

## Deployment

A multi-arch image (amd64, arm64) is published to `ghcr.io/hezflix/bigarr` on every merge to `main`. See [`deploy/docker-compose.example.yml`](deploy/docker-compose.example.yml). Downloads and libraries must live under **one shared mount**, otherwise hardlinks are impossible and imports fall back to copies.

## License

GPL-3.0-or-later. The release parser is vendored from [Radarr](https://github.com/Radarr/Radarr) (GPL-3.0); see `src/Bigarr.Parsing/VENDORED.md` for the inventory and the upstream sync procedure.
