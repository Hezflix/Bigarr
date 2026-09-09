namespace Bigarr.Api.Tests.Metadata;

/// <summary>
/// Hand-captured shapes of TMDB API v3 responses (September 2026), trimmed to the fields Bigarr reads plus a few it
/// ignores, so the mapping is exercised against realistic payloads without touching the network.
/// </summary>
public static class TmdbRecorded
{
    public const string Movie603 = """
    {
      "adult": false,
      "backdrop_path": "/icmmSD4vTTDKOq2vvdulafOGw93.jpg",
      "belongs_to_collection": { "id": 2344, "name": "The Matrix Collection", "poster_path": "/bV9qTVHTVf0gkW0j7p7M0ILD4pG.jpg", "backdrop_path": "/bRm2DEgUiYciDw3myHuYFInD7la.jpg" },
      "budget": 63000000,
      "genres": [ { "id": 28, "name": "Action" }, { "id": 878, "name": "Science Fiction" } ],
      "homepage": "http://www.warnerbros.com/matrix",
      "id": 603,
      "imdb_id": "tt0133093",
      "origin_country": [ "US" ],
      "original_language": "en",
      "original_title": "The Matrix",
      "overview": "Set in the 22nd century, The Matrix tells the story of a computer hacker who joins a group of underground insurgents fighting the vast and powerful computers who now rule the earth.",
      "popularity": 80.123,
      "poster_path": "/p96dm7sCMn4VYAStA6siNz30G1r.jpg",
      "production_companies": [ { "id": 79, "logo_path": "/at4uYdwAAgNRKhZuuFX8ShKSybw.png", "name": "Village Roadshow Pictures", "origin_country": "US" } ],
      "release_date": "1999-03-31",
      "revenue": 463517383,
      "runtime": 136,
      "status": "Released",
      "tagline": "Believe the unbelievable.",
      "title": "The Matrix",
      "video": false,
      "vote_average": 8.2,
      "vote_count": 25000,
      "alternative_titles": {
        "titles": [
          { "iso_3166_1": "FR", "title": "Matrix", "type": "" },
          { "iso_3166_1": "US", "title": "The Matrix", "type": "working title" },
          { "iso_3166_1": "RU", "title": "Матрица", "type": "" }
        ]
      },
      "release_dates": {
        "results": [
          { "iso_3166_1": "US", "release_dates": [
              { "certification": "", "descriptors": [], "iso_639_1": "", "note": "Westwood, California", "release_date": "1999-03-24T00:00:00.000Z", "type": 1 },
              { "certification": "R", "descriptors": [], "iso_639_1": "", "note": "", "release_date": "1999-03-31T00:00:00.000Z", "type": 3 },
              { "certification": "R", "descriptors": [], "iso_639_1": "", "note": "DVD", "release_date": "1999-09-21T00:00:00.000Z", "type": 5 }
          ] },
          { "iso_3166_1": "FR", "release_dates": [
              { "certification": "", "descriptors": [], "iso_639_1": "", "note": "", "release_date": "1999-06-23T00:00:00.000Z", "type": 3 },
              { "certification": "", "descriptors": [], "iso_639_1": "", "note": "", "release_date": "2010-12-01T00:00:00.000Z", "type": 4 }
          ] }
        ]
      },
      "external_ids": { "imdb_id": "tt0133093", "wikidata_id": "Q83495", "facebook_id": "TheMatrixMovie", "instagram_id": null, "twitter_id": null }
    }
    """;

    public const string SearchMatrix = """
    {
      "page": 1,
      "results": [
        { "adult": false, "backdrop_path": "/icmmSD4vTTDKOq2vvdulafOGw93.jpg", "genre_ids": [ 28, 878 ], "id": 603, "original_language": "en", "original_title": "The Matrix", "overview": "Set in the 22nd century...", "popularity": 80.1, "poster_path": "/p96dm7sCMn4VYAStA6siNz30G1r.jpg", "release_date": "1999-03-31", "title": "The Matrix", "video": false, "vote_average": 8.2, "vote_count": 25000 },
        { "adult": false, "backdrop_path": null, "genre_ids": [ 878, 12 ], "id": 999999, "original_language": "en", "original_title": "The Matrix: Rebirth", "overview": "", "popularity": 1.0, "poster_path": null, "release_date": "2028-05-01", "title": "The Matrix: Rebirth", "video": false, "vote_average": 0, "vote_count": 0 }
      ],
      "total_pages": 1,
      "total_results": 2
    }
    """;

    public const string FindTt0133093 = """
    { "movie_results": [ { "adult": false, "id": 603, "title": "The Matrix", "original_title": "The Matrix", "release_date": "1999-03-31", "genre_ids": [ 28, 878 ], "media_type": "movie" } ], "person_results": [], "tv_results": [], "tv_episode_results": [], "tv_season_results": [] }
    """;

    public const string NotFound = """
    { "success": false, "status_code": 34, "status_message": "The resource you requested could not be found." }
    """;
}
