namespace Bigarr.Api.Tests.Indexers;

/// <summary>Trimmed shape of Prowlarr's <c>GET /api/v1/indexer</c> (v2, September 2026).</summary>
public static class ProwlarrRecorded
{
    public const string Indexers = """
    [
      {
        "indexerUrls": ["https://tracker-one.example/"],
        "legacyUrls": [],
        "definitionName": "trackerone",
        "description": "Tracker One is a private tracker",
        "language": "en-US",
        "encoding": "Unicode (UTF-8)",
        "enable": true,
        "redirect": false,
        "supportsRss": true,
        "supportsSearch": true,
        "supportsRedirect": false,
        "supportsPagination": true,
        "appProfileId": 1,
        "protocol": "torrent",
        "privacy": "private",
        "capabilities": {
          "limitsMax": 100,
          "limitsDefault": 100,
          "categories": [
            { "id": 2000, "name": "Movies", "subCategories": [ { "id": 2040, "name": "Movies/HD", "subCategories": [] }, { "id": 2045, "name": "Movies/UHD", "subCategories": [] } ] },
            { "id": 5000, "name": "TV", "subCategories": [] }
          ],
          "supportsRawSearch": false,
          "searchParams": ["q"],
          "tvSearchParams": ["q", "season", "ep", "imdbId", "tvdbId"],
          "movieSearchParams": ["q", "imdbId", "tmdbId"],
          "musicSearchParams": ["q"],
          "bookSearchParams": ["q"]
        },
        "priority": 10,
        "downloadClientId": 0,
        "added": "2026-08-01T10:00:00Z",
        "sortName": "tracker one",
        "name": "Tracker One",
        "fields": [ { "order": 0, "name": "baseUrl", "label": "Base Url", "value": "https://tracker-one.example/", "type": "select", "advanced": false } ],
        "implementationName": "Cardigann",
        "implementation": "Cardigann",
        "configContract": "CardigannSettings",
        "infoLink": "https://wiki.servarr.com/prowlarr/supported-indexers#trackerone",
        "tags": [],
        "id": 1
      },
      {
        "indexerUrls": ["https://public.example/"],
        "definitionName": "publicone",
        "enable": false,
        "supportsRss": true,
        "supportsSearch": true,
        "protocol": "torrent",
        "privacy": "public",
        "capabilities": { "categories": [], "movieSearchParams": ["q"] },
        "priority": 40,
        "name": "Public One",
        "implementation": "Cardigann",
        "tags": [],
        "id": 7
      },
      {
        "indexerUrls": ["https://nzb.example/"],
        "definitionName": "nzbgeek",
        "enable": true,
        "supportsRss": true,
        "supportsSearch": true,
        "protocol": "usenet",
        "privacy": "private",
        "capabilities": { "categories": [], "movieSearchParams": ["q", "imdbId"] },
        "priority": 25,
        "name": "NZB Geek",
        "implementation": "Newznab",
        "tags": [],
        "id": 12
      }
    ]
    """;

    public const string Status = """
    { "appName": "Prowlarr", "instanceName": "Prowlarr", "version": "2.3.0.5321", "buildTime": "2026-08-20T00:00:00Z", "isDebug": false, "isProduction": true, "isAdmin": false, "isUserInteractive": false, "startupPath": "/app/prowlarr/bin", "appData": "/config", "osName": "ubuntu", "osVersion": "24.04", "isDocker": true, "mode": "console", "branch": "master", "authentication": "forms", "databaseType": "sqLite", "databaseVersion": "3.46.1", "runtimeVersion": "8.0.12", "runtimeName": "netCore", "startTime": "2026-09-01T00:00:00Z", "packageVersion": "2.3.0.5321-ls30", "packageAuthor": "linuxserver.io", "packageUpdateMechanism": "docker" }
    """;
}
