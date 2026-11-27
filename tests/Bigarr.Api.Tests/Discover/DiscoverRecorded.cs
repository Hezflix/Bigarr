namespace Bigarr.Api.Tests.Discover;

/// <summary>
/// TMDB API v3 answers in the shapes Discover reads (lists, title pages, a person, genres), trimmed to a few entries and
/// the fields it uses plus some it ignores, so the mapping runs against realistic payloads without the network.
/// </summary>
public static class DiscoverRecorded
{
    public const string TrendingWeek = """
    {
      "page": 1,
      "results": [
        { "adult": false, "backdrop_path": "/icmmSD4vTTDKOq2vvdulafOGw93.jpg", "id": 603, "title": "The Matrix", "original_title": "The Matrix", "overview": "Set in the 22nd century...", "poster_path": "/p96dm7sCMn4VYAStA6siNz30G1r.jpg", "media_type": "movie", "original_language": "en", "genre_ids": [28, 878], "popularity": 80.1, "release_date": "1999-03-31", "video": false, "vote_average": 8.2, "vote_count": 25000 },
        { "adult": false, "backdrop_path": "/tsRy63Mu5cu8etL1X7ZLyf7UP1M.jpg", "id": 1396, "name": "Breaking Bad", "original_name": "Breaking Bad", "overview": "Walter White, a New Mexico chemistry teacher...", "poster_path": "/ztkUQFLlC19CCMYHW9o1zWhJRNq.jpg", "media_type": "tv", "original_language": "en", "genre_ids": [18, 80], "popularity": 400.5, "first_air_date": "2008-01-20", "vote_average": 8.9, "vote_count": 15000, "origin_country": ["US"] },
        { "adult": false, "id": 6384, "name": "Keanu Reeves", "original_name": "Keanu Reeves", "media_type": "person", "popularity": 60.2, "gender": 2, "known_for_department": "Acting", "profile_path": "/4D0PpNI0kmP58hgrwGC3wCjxhnm.jpg" },
        { "adult": false, "id": 157336, "title": "Interstellar", "original_title": "Interstellar", "overview": "The adventures of a group of explorers...", "poster_path": "/gEU2QniE6E77NI6lCU6MxlNBvIx.jpg", "media_type": "movie", "genre_ids": [12, 18, 878], "popularity": 120.3, "release_date": "2014-11-05", "vote_average": 8.4, "vote_count": 35000 }
      ],
      "total_pages": 1000,
      "total_results": 20000
    }
    """;

    public const string PopularMovies = """
    {
      "page": 2,
      "results": [
        { "adult": false, "backdrop_path": "/rcjK4Ul2qJnMWvSPEMYIDzYwlJV.jpg", "id": 604, "title": "The Matrix Reloaded", "original_title": "The Matrix Reloaded", "overview": "Six months after the events depicted in The Matrix...", "poster_path": "/9TGHDvWrqKBzwDxDodHYXEmOE6J.jpg", "genre_ids": [12, 28], "popularity": 50.2, "release_date": "2003-05-15", "video": false, "vote_average": 7.0, "vote_count": 12000 },
        { "adult": false, "id": 27205, "title": "Inception", "original_title": "Inception", "overview": "Cobb, a skilled thief...", "poster_path": null, "genre_ids": [28, 878, 12], "popularity": 90.0, "release_date": "2010-07-15", "video": false, "vote_average": 0, "vote_count": 0 }
      ],
      "total_pages": 48000,
      "total_results": 960000
    }
    """;

    public const string MovieGenres = """
    { "genres": [ { "id": 28, "name": "Action" }, { "id": 16, "name": "Animation" }, { "id": 878, "name": "Science Fiction" } ] }
    """;

    public const string SeriesGenres = """
    { "genres": [ { "id": 18, "name": "Drama" }, { "id": 10765, "name": "Sci-Fi & Fantasy" } ] }
    """;

    public const string Company3 = """
    { "description": "", "headquarters": "Emeryville, California, United States", "homepage": "https://www.pixar.com", "id": 3, "logo_path": "/1TjvGVDMYsj6JBxOAkUHpPEwLf7.png", "name": "Pixar Animation Studios", "origin_country": "US", "parent_company": null }
    """;

    public const string Network213 = """
    { "headquarters": "Los Gatos, California, United States", "homepage": "https://www.netflix.com", "id": 213, "logo_path": "/wwemzKWzjKYJFfCeiB57q3r4Bcm.png", "name": "Netflix", "origin_country": "" }
    """;

    public const string Movie603 = """
    {
      "adult": false,
      "backdrop_path": "/icmmSD4vTTDKOq2vvdulafOGw93.jpg",
      "genres": [ { "id": 28, "name": "Action" }, { "id": 878, "name": "Science Fiction" } ],
      "id": 603,
      "imdb_id": "tt0133093",
      "original_title": "The Matrix",
      "overview": "Set in the 22nd century, The Matrix tells the story of a computer hacker who joins a group of underground insurgents fighting the vast and powerful computers who now rule the earth.",
      "poster_path": "/p96dm7sCMn4VYAStA6siNz30G1r.jpg",
      "production_companies": [ { "id": 79, "logo_path": "/at4uYdwAAgNRKhZuuFX8ShKSybw.png", "name": "Village Roadshow Pictures", "origin_country": "US" }, { "id": 174, "logo_path": null, "name": "Warner Bros. Pictures", "origin_country": "US" } ],
      "release_date": "1999-03-31",
      "runtime": 136,
      "status": "Released",
      "tagline": "Believe the unbelievable.",
      "title": "The Matrix",
      "vote_average": 8.2,
      "vote_count": 25000,
      "credits": {
        "cast": [
          { "adult": false, "id": 2975, "name": "Laurence Fishburne", "character": "Morpheus", "profile_path": "/iwx7h0AfUGGsKNsmTV7mR1zZDfB.jpg", "order": 1 },
          { "adult": false, "id": 6384, "name": "Keanu Reeves", "character": "Thomas A. Anderson / Neo", "profile_path": "/4D0PpNI0kmP58hgrwGC3wCjxhnm.jpg", "order": 0 }
        ],
        "crew": [
          { "id": 9339, "name": "Lilly Wachowski", "department": "Directing", "job": "Director" },
          { "id": 9340, "name": "Lana Wachowski", "department": "Directing", "job": "Director" },
          { "id": 9340, "name": "Lana Wachowski", "department": "Writing", "job": "Writer" }
        ]
      },
      "videos": {
        "results": [
          { "iso_639_1": "en", "iso_3166_1": "US", "name": "Teaser", "key": "teaser-key", "site": "YouTube", "type": "Teaser", "official": true },
          { "iso_639_1": "en", "iso_3166_1": "US", "name": "Fan trailer", "key": "fan-key", "site": "YouTube", "type": "Trailer", "official": false },
          { "iso_639_1": "en", "iso_3166_1": "US", "name": "Official Trailer", "key": "vKQi3bBA1y8", "site": "YouTube", "type": "Trailer", "official": true }
        ]
      },
      "recommendations": { "page": 1, "results": [ { "adult": false, "id": 604, "title": "The Matrix Reloaded", "media_type": "movie", "release_date": "2003-05-15", "poster_path": "/9TGHDvWrqKBzwDxDodHYXEmOE6J.jpg", "vote_average": 7.0 } ], "total_pages": 1, "total_results": 1 },
      "similar": { "page": 1, "results": [ { "adult": false, "id": 157336, "title": "Interstellar", "release_date": "2014-11-05", "poster_path": "/gEU2QniE6E77NI6lCU6MxlNBvIx.jpg", "vote_average": 8.4 } ], "total_pages": 1, "total_results": 1 },
      "release_dates": { "results": [ { "iso_3166_1": "US", "release_dates": [ { "certification": "", "type": 1, "release_date": "1999-03-24T00:00:00.000Z" }, { "certification": "R", "type": 3, "release_date": "1999-03-31T00:00:00.000Z" } ] } ] }
    }
    """;

    public const string Tv1396 = """
    {
      "adult": false,
      "backdrop_path": "/tsRy63Mu5cu8etL1X7ZLyf7UP1M.jpg",
      "created_by": [ { "id": 66633, "name": "Vince Gilligan", "profile_path": "/z3E0DhBg1V1PZVEtS9vfFPzOWYB.jpg" } ],
      "episode_run_time": [ 45, 47 ],
      "first_air_date": "2008-01-20",
      "genres": [ { "id": 18, "name": "Drama" }, { "id": 80, "name": "Crime" } ],
      "id": 1396,
      "last_air_date": "2013-09-29",
      "name": "Breaking Bad",
      "networks": [ { "id": 174, "logo_path": "/alqLicR1ZMHMaZGP3xRQxn9sq7p.png", "name": "AMC", "origin_country": "US" } ],
      "number_of_episodes": 62,
      "number_of_seasons": 5,
      "original_name": "Breaking Bad",
      "overview": "Walter White, a New Mexico chemistry teacher, is diagnosed with Stage III cancer...",
      "poster_path": "/ztkUQFLlC19CCMYHW9o1zWhJRNq.jpg",
      "seasons": [
        { "air_date": "2009-02-17", "episode_count": 9, "id": 3577, "name": "Specials", "overview": "", "poster_path": null, "season_number": 0 },
        { "air_date": "2008-01-20", "episode_count": 7, "id": 3572, "name": "Season 1", "overview": "", "poster_path": "/1BP4xYv9ZG4ZVHkL7ocOziBbSYH.jpg", "season_number": 1 },
        { "air_date": "2009-03-08", "episode_count": 13, "id": 3573, "name": "Season 2", "overview": "", "poster_path": "/e3oGYpoTUhOFK0BJfloru5ZmGV.jpg", "season_number": 2 },
        { "air_date": "2010-03-21", "episode_count": 13, "id": 3575, "name": "Season 3", "overview": "", "poster_path": null, "season_number": 3 }
      ],
      "status": "Ended",
      "tagline": "Change the equation.",
      "vote_average": 8.9,
      "credits": { "cast": [ { "id": 17419, "name": "Bryan Cranston", "character": "Walter White", "profile_path": "/7Jahy5LZX2Fo8fGJltMreAI49hC.jpg", "order": 0 } ], "crew": [] },
      "videos": { "results": [] },
      "recommendations": { "page": 1, "results": [ { "adult": false, "id": 60059, "name": "Better Call Saul", "media_type": "tv", "first_air_date": "2015-02-08", "poster_path": "/fC2HDm5t0kHl7mTm7jxMR31b7by.jpg", "vote_average": 8.7 } ], "total_pages": 1, "total_results": 1 },
      "similar": { "page": 1, "results": [], "total_pages": 1, "total_results": 0 },
      "external_ids": { "imdb_id": "tt0903747", "tvdb_id": 81189, "tvrage_id": 18164 }
    }
    """;

    public const string SearchMulti = """
    {
      "page": 1,
      "results": [
        { "adult": false, "id": 6384, "name": "Keanu Reeves", "media_type": "person", "known_for_department": "Acting", "profile_path": "/4D0PpNI0kmP58hgrwGC3wCjxhnm.jpg", "popularity": 60.2 },
        { "adult": false, "id": 603, "title": "The Matrix", "media_type": "movie", "release_date": "1999-03-31", "poster_path": "/p96dm7sCMn4VYAStA6siNz30G1r.jpg", "vote_average": 8.2 },
        { "adult": true, "id": 99, "title": "Not for here", "media_type": "movie", "release_date": "2001-01-01" }
      ],
      "total_pages": 1,
      "total_results": 3
    }
    """;

    public const string Person6384 = """
    {
      "adult": false,
      "biography": "Keanu Charles Reeves is a Canadian actor.",
      "birthday": "1964-09-02",
      "deathday": null,
      "id": 6384,
      "known_for_department": "Acting",
      "name": "Keanu Reeves",
      "place_of_birth": "Beirut, Lebanon",
      "profile_path": "/4D0PpNI0kmP58hgrwGC3wCjxhnm.jpg",
      "combined_credits": {
        "cast": [
          { "adult": false, "id": 603, "title": "The Matrix", "media_type": "movie", "character": "Neo", "release_date": "1999-03-31", "poster_path": "/p96dm7sCMn4VYAStA6siNz30G1r.jpg", "popularity": 80.1 },
          { "adult": false, "id": 604, "title": "The Matrix Reloaded", "media_type": "movie", "character": "Neo", "release_date": "2003-05-15", "popularity": 50.2 }
        ],
        "crew": [
          { "adult": false, "id": 603, "title": "The Matrix", "media_type": "movie", "job": "Stunts", "department": "Crew", "release_date": "1999-03-31", "popularity": 80.1 }
        ]
      }
    }
    """;
}
