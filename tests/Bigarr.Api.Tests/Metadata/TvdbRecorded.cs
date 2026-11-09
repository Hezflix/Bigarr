namespace Bigarr.Api.Tests.Metadata;

/// <summary>
/// Captured TVDB v4 and TMDB TV answers (November 2026), trimmed to the fields Bigarr reads plus a few it ignores,
/// so the series mapping and the TVDB↔TMDB linking run against real shapes without the network or a key.
/// </summary>
public static class TvdbRecorded
{
    /// <summary><c>GET /v4/series/81189/extended?meta=translations&amp;short=true</c>, trimmed.</summary>
    public const string SeriesExtended81189 = """
    {
      "status": "success",
      "data": {
        "id": 81189,
        "name": "Breaking Bad",
        "slug": "breaking-bad",
        "image": "https://artworks.thetvdb.com/banners/posters/81189-10.jpg",
        "firstAired": "2008-01-20",
        "lastAired": "2013-09-29",
        "nextAired": "",
        "status": {
          "id": 2,
          "name": "Ended",
          "recordType": "series",
          "keepUpdated": false
        },
        "originalCountry": "usa",
        "originalLanguage": "eng",
        "defaultSeasonType": 1,
        "averageRuntime": 48,
        "overview": "When Walter White, a chemistry teacher, is diagnosed with Stage III cancer and given a prognosis of two years left to live, he chooses to enter a dangerous world of drugs and crime with the intent to secure his family's financial security.",
        "year": "2008",
        "airsTime": "21:00",
        "originalNetwork": {
          "id": 389,
          "name": "AMC",
          "country": "usa"
        },
        "latestNetwork": {
          "id": 389,
          "name": "AMC",
          "country": "usa"
        },
        "genres": [
          {
            "id": 12,
            "name": "Drama",
            "slug": "drama"
          },
          {
            "id": 14,
            "name": "Crime",
            "slug": "crime"
          },
          {
            "id": 24,
            "name": "Thriller",
            "slug": "thriller"
          },
          {
            "id": 26,
            "name": "Western",
            "slug": "western"
          }
        ],
        "aliases": [
          {
            "language": "zho",
            "name": "絕命毒師"
          },
          {
            "language": "eng",
            "name": "Breaking Bad: Original Minisodes"
          },
          {
            "language": "ita",
            "name": "Breaking Bad - Reazioni collaterali"
          }
        ],
        "remoteIds": [
          {
            "id": "tt0903747",
            "type": 2,
            "sourceName": "IMDB"
          },
          {
            "id": "https://www.amc.com/shows/breaking-bad",
            "type": 4,
            "sourceName": "Official Website"
          },
          {
            "id": "1396",
            "type": 12,
            "sourceName": "TheMovieDB.com"
          },
          {
            "id": "EP01009396",
            "type": 3,
            "sourceName": "TMS (Zap2It)"
          }
        ],
        "seasons": [
          {
            "id": 30272,
            "seriesId": 81189,
            "type": {
              "id": 1,
              "name": "Aired Order",
              "type": "official",
              "alternateName": null
            },
            "number": 1,
            "image": "https://artworks.thetvdb.com/banners/seasons/81189-1-3.jpg"
          },
          {
            "id": 40719,
            "seriesId": 81189,
            "type": {
              "id": 1,
              "name": "Aired Order",
              "type": "official",
              "alternateName": null
            },
            "number": 2,
            "image": "https://artworks.thetvdb.com/banners/seasons/81189-2-17.jpg"
          },
          {
            "id": 439371,
            "seriesId": 81189,
            "type": {
              "id": 1,
              "name": "Aired Order",
              "type": "official",
              "alternateName": null
            },
            "number": 0,
            "image": "https://artworks.thetvdb.com/banners/seasons/81189-0.jpg"
          },
          {
            "id": 1726855,
            "seriesId": 81189,
            "type": {
              "id": 2,
              "name": "DVD Order",
              "type": "dvd",
              "alternateName": null
            },
            "number": 0,
            "image": "https://artworks.thetvdb.com/banners/seasons/81189-0-2.jpg"
          },
          {
            "id": 1726856,
            "seriesId": 81189,
            "type": {
              "id": 2,
              "name": "DVD Order",
              "type": "dvd",
              "alternateName": null
            },
            "number": 1,
            "image": "https://artworks.thetvdb.com/banners/seasons/81189-1-10.jpg"
          },
          {
            "id": 1726857,
            "seriesId": 81189,
            "type": {
              "id": 2,
              "name": "DVD Order",
              "type": "dvd",
              "alternateName": null
            },
            "number": 2,
            "image": "https://artworks.thetvdb.com/banners/seasons/81189-2-12.jpg"
          }
        ],
        "translations": {
          "nameTranslations": [
            {
              "name": "Breaking Bad",
              "language": "deu"
            },
            {
              "name": "Breaking Bad",
              "language": "eng",
              "isPrimary": true
            },
            {
              "name": "Breaking Bad",
              "language": "fra"
            },
            {
              "name": "Breaking Bad: Original Minisodes",
              "isAlias": true,
              "language": "eng"
            }
          ],
          "overviewTranslations": [
            {
              "overview": "When Walter White, a chemistry teacher, is diagnosed with Stage III cancer and given a prognosis of two years left to live, he chooses to enter a dangerous world of drugs and crime with the intent to secure his family's financial security.",
              "language": "eng",
              "isPrimary": true
            },
            {
              "overview": "La vie de Walter White, professeur de chimie dans un lycée, est bouleversée lorsqu'il apprend qu'il est atteint d'un cancer en phase terminale. Une nouvelle qui le sort de la torpeur de son quotidien et l'amène à prendre des mesures radicales pour anticiper l'avenir de sa famille.",
              "language": "fra"
            }
          ]
        }
      }
    }
    """;

    /// <summary><c>GET /v4/series/81189/episodes/default/eng?page=0</c>: one special, three regular episodes.</summary>
    public const string Episodes81189 = """
    {
      "status": "success",
      "data": {
        "id": 81189,
        "name": "Breaking Bad",
        "episodes": [
          {
            "id": 3859781,
            "seriesId": 81189,
            "name": "Good Cop / Bad Cop",
            "aired": "2009-02-17",
            "runtime": 3,
            "overview": "Hank and Marie try to spice up their relationship on Valentine's Day.",
            "image": "/banners/episodes/81189/3859781.jpg",
            "number": 1,
            "absoluteNumber": 0,
            "seasonNumber": 0
          },
          {
            "id": 349232,
            "seriesId": 81189,
            "name": "Pilot",
            "aired": "2008-01-20",
            "runtime": 58,
            "overview": "When an unassuming high school chemistry teacher discovers he has a rare form of lung cancer, he decides to team up with a former student and create a top of the line crystal meth in a used RV, to provide for his family once he is gone.",
            "image": "/banners/episodes/81189/6421d9584f9dd.jpg",
            "number": 1,
            "absoluteNumber": 1,
            "seasonNumber": 1
          },
          {
            "id": 349233,
            "seriesId": 81189,
            "name": "Cat's in the Bag...",
            "aired": "2008-01-27",
            "runtime": 48,
            "overview": "Walt and Jesse attempt to tie up loose ends. The desperate situation gets more complicated with the flip of a coin. Walt's wife, Skyler, becomes suspicious of Walt's strange behavior.",
            "image": "/banners/episodes/81189/642203f37c277.jpg",
            "number": 2,
            "absoluteNumber": 2,
            "seasonNumber": 1
          },
          {
            "id": 438912,
            "seriesId": 81189,
            "name": "Seven Thirty-Seven",
            "aired": "2009-03-08",
            "runtime": 47,
            "overview": "Walt and Jesse are reminded of Tuco’s volatile nature, and try to figure a way out of their business partnership. Hank attempts to mend fences between the estranged Marie and Skyler.",
            "image": "/banners/series/81189/episodes/5ea87588d39fc.jpg",
            "number": 1,
            "absoluteNumber": 8,
            "seasonNumber": 2
          }
        ]
      },
      "links": {
        "prev": null,
        "self": "https://api4.thetvdb.com/v4/series/81189/episodes/default/eng?page=0",
        "next": null,
        "total_items": 4,
        "page_size": 500
      }
    }
    """;

    /// <summary>TMDB <c>GET /3/tv/1396?append_to_response=external_ids,alternative_titles</c>, trimmed.</summary>
    public const string TmdbTv1396 = """
    {
      "id": 1396,
      "name": "Breaking Bad",
      "original_name": "Breaking Bad",
      "original_language": "en",
      "origin_country": [
        "US"
      ],
      "overview": "Walter White, a New Mexico chemistry teacher, is diagnosed with Stage III cancer and given a prognosis of only two years left to live. He becomes filled with a sense of fearlessness and an unrelenting desire to secure his family's financial future at any cost as he enters the dangerous world of drugs and crime.",
      "first_air_date": "2008-01-20",
      "last_air_date": "2013-09-29",
      "status": "Ended",
      "type": "Scripted",
      "episode_run_time": [],
      "poster_path": "/anFx9aTOOYqgS3v7x3R84Kz67ly.jpg",
      "backdrop_path": "/tsRy63Mu5cu8etL1X7ZLyf7UP1M.jpg",
      "genres": [
        {
          "id": 18,
          "name": "Drama"
        },
        {
          "id": 80,
          "name": "Crime"
        }
      ],
      "networks": [
        {
          "id": 174,
          "name": "AMC"
        }
      ],
      "seasons": [
        {
          "season_number": 0,
          "name": "Specials",
          "overview": "",
          "poster_path": "/4RaIvSQgHHHPfaI1jFqaMR8UJWM.jpg",
          "episode_count": 9,
          "id": 3577
        },
        {
          "season_number": 1,
          "name": "Season 1",
          "overview": "High school chemistry teacher Walter White's life is suddenly transformed by a dire medical diagnosis. Street-savvy former student Jesse Pinkman \"teaches\" Walter a new trade.",
          "poster_path": "/1BP4xYv9ZG4ZVHkL7ocOziBbSYH.jpg",
          "episode_count": 7,
          "id": 3572
        },
        {
          "season_number": 2,
          "name": "Season 2",
          "overview": "Walt must deal with the chain reaction of his choice, as he and Jesse face new and severe consequences. When danger and suspicion around Walt escalate, he is pushed to new levels of desperation. Just how much higher will the stakes rise? How far is Walt willing to go to ensure his family's security? Will his grand plan spiral out of control?",
          "poster_path": "/e3oGYpoTUhOFK0BJfloru5ZmGV.jpg",
          "episode_count": 13,
          "id": 3573
        }
      ],
      "external_ids": {
        "imdb_id": "tt0903747",
        "tvdb_id": 81189,
        "tvrage_id": 18164
      },
      "alternative_titles": {
        "results": [
          {
            "iso_3166_1": "AZ",
            "title": "Bədbəxt",
            "type": ""
          },
          {
            "iso_3166_1": "BR",
            "title": "Breaking Bad: A Química do Mal",
            "type": "altered title for broadcast on tv channel Record"
          }
        ]
      }
    }
    """;

    /// <summary>TMDB <c>GET /3/tv/1396?append_to_response=season/0,season/1,season/2</c>, the appended seasons only.</summary>
    public const string TmdbTv1396Seasons = """
    {
      "id": 1396,
      "name": "Breaking Bad",
      "season/0": {
        "season_number": 0,
        "name": "Specials",
        "overview": "",
        "poster_path": "/4RaIvSQgHHHPfaI1jFqaMR8UJWM.jpg",
        "episodes": [
          {
            "id": 62131,
            "season_number": 0,
            "episode_number": 1,
            "name": "Good Cop / Bad Cop",
            "overview": "Season 1 Minisode 1: Hank and Marie celebrate Valentine's Day.",
            "air_date": "2009-02-17",
            "runtime": 3,
            "still_path": "/t729tFVXPetnJlJ2VsUZQz0rX6v.jpg"
          }
        ]
      },
      "season/1": {
        "season_number": 1,
        "name": "Season 1",
        "overview": "High school chemistry teacher Walter White's life is suddenly transformed by a d",
        "poster_path": "/1BP4xYv9ZG4ZVHkL7ocOziBbSYH.jpg",
        "episodes": [
          {
            "id": 62085,
            "season_number": 1,
            "episode_number": 1,
            "name": "Pilot",
            "overview": "When an unassuming high school chemistry teacher discovers he has a rare form of",
            "air_date": "2008-01-20",
            "runtime": 59,
            "still_path": "/ydlY3iPfeOAvu8gVqrxPoMvzNCn.jpg"
          },
          {
            "id": 62086,
            "season_number": 1,
            "episode_number": 2,
            "name": "Cat's in the Bag...",
            "overview": "Walt and Jesse attempt to tie up loose ends. The desperate situation gets more c",
            "air_date": "2008-01-27",
            "runtime": 49,
            "still_path": "/AbMoecO0ZZio0LcgeLxlzdyGs6X.jpg"
          }
        ]
      },
      "season/2": {
        "season_number": 2,
        "name": "Season 2",
        "overview": "Walt must deal with the chain reaction of his choice, as he and Jesse face new a",
        "poster_path": "/e3oGYpoTUhOFK0BJfloru5ZmGV.jpg",
        "episodes": [
          {
            "id": 972873,
            "season_number": 2,
            "episode_number": 1,
            "name": "Seven Thirty-Seven",
            "overview": "Walt and Jesse are vividly reminded of Tuco’s volatile nature, and try to figure",
            "air_date": "2009-03-08",
            "runtime": 48,
            "still_path": "/6Uo1z56uKnX60JXvYYFWACHR31u.jpg"
          }
        ]
      }
    }
    """;

    /// <summary>TMDB <c>GET /3/search/tv?query=one piece</c>: the anime first, then the live-action show.</summary>
    public const string TmdbSearchOnePiece = """
    {
      "page": 1,
      "results": [
        {
          "id": 37854,
          "name": "One Piece",
          "original_name": "ワンピース",
          "original_language": "ja",
          "origin_country": [
            "JP"
          ],
          "overview": "Years ago, the fearsome Pirate King, Gol D. Roger was executed leaving a huge pi",
          "first_air_date": "1999-10-20",
          "poster_path": "/dB4EDhre2dsC2kxYDavyKWqLQwi.jpg",
          "backdrop_path": "/2rmK7mnchw9Xr3XdiTFSxTTLXqv.jpg",
          "genre_ids": [
            10759,
            35,
            16
          ]
        },
        {
          "id": 111110,
          "name": "ONE PIECE",
          "original_name": "ONE PIECE",
          "original_language": "en",
          "origin_country": [
            "US"
          ],
          "overview": "With his straw hat and ragtag crew, young pirate Monkey D. Luffy goes on an epic",
          "first_air_date": "2023-08-31",
          "poster_path": "/blWCPEqDGLBuLB9u89CxP9ORQP4.jpg",
          "backdrop_path": "/qD211Hb5XwFxrszzBBe5EUYJerh.jpg",
          "genre_ids": [
            10759,
            10765
          ]
        }
      ],
      "total_pages": 1,
      "total_results": 2
    }
    """;

    /// <summary>TVDB <c>GET /v4/search?query=one piece&amp;type=series</c>: the anime is not among the first hits.</summary>
    public const string TvdbSearchOnePiece = """
    {
      "status": "success",
      "data": [
        {
          "objectID": "series-392276",
          "country": "usa",
          "id": "series-392276",
          "image_url": "https://artworks.thetvdb.com/banners/v4/series/392276/posters/698b6d19dd600.jpg",
          "name": "ONE PIECE (2023)",
          "first_air_time": "2023-08-31",
          "overview": "With his straw hat and ragtag crew, young pirate Monkey D. Luffy goes on an epic",
          "primary_language": "eng",
          "status": "Continuing",
          "type": "series",
          "tvdb_id": "392276",
          "year": "2023",
          "network": "Netflix",
          "remote_ids": [
            {
              "id": "OnePieceNetflix",
              "type": 5,
              "sourceName": "Facebook"
            },
            {
              "id": "tt11737520",
              "type": 2,
              "sourceName": "IMDB"
            },
            {
              "id": "onepiecenetflix",
              "type": 9,
              "sourceName": "Instagram"
            },
            {
              "id": "https://www.netflix.com/title/80217863",
              "type": 4,
              "sourceName": "Official Website"
            },
            {
              "id": "111110",
              "type": 12,
              "sourceName": "TheMovieDB.com"
            },
            {
              "id": "46065",
              "type": 19,
              "sourceName": "TV Maze"
            },
            {
              "id": "onepiecenetflix",
              "type": 6,
              "sourceName": "X (Twitter)"
            },
            {
              "id": "Q109526557",
              "type": 18,
              "sourceName": "Wikidata"
            },
            {
              "id": "One_Piece_(2023_TV_series)",
              "type": 24,
              "sourceName": "Wikipedia"
            }
          ],
          "translations": {
            "eng": "ONE PIECE (2023)",
            "jpn": "ONE PIECE (実写ドラマ)"
          },
          "overviews": {
            "eng": "With his straw hat and ragtag crew, young pirate Monkey D. Luffy goes on an epic"
          },
          "aliases": [
            "ONE PIECE: THE LIVE ACTION SERIES",
            "ONE PIECE (US)",
            "One Piece",
            "One Piece",
            "One Piece",
            "One Piece. Большой куш",
            "航海王",
            "Великий Куш"
          ]
        },
        {
          "objectID": "series-464521",
          "country": "jpn",
          "id": "series-464521",
          "image_url": "https://artworks.thetvdb.com/banners/v4/series/464521/posters/684c46a850bbc.jpg",
          "name": "THE ONE PIECE",
          "first_air_time": null,
          "overview": "東の海（イーストブルー）の端に位置する、フーシャ村。\r\n村の少年、モンキー・Ｄ・ルフィは、悪魔の実を食べたことによって体がゴムのように伸びる、ゴム人間になってし",
          "primary_language": "jpn",
          "status": "Upcoming",
          "type": "series",
          "tvdb_id": "464521",
          "year": null,
          "network": "Netflix",
          "remote_ids": [
            {
              "id": "tt30476502",
              "type": 2,
              "sourceName": "IMDB"
            },
            {
              "id": "https://www.netflix.com/title/81594316",
              "type": 4,
              "sourceName": "Official Website"
            },
            {
              "id": "241709",
              "type": 12,
              "sourceName": "TheMovieDB.com"
            },
            {
              "id": "Q123893209",
              "type": 18,
              "sourceName": "Wikidata"
            }
          ],
          "translations": {
            "eng": "THE ONE PIECE",
            "jpn": "THE ONE PIECE"
          },
          "overviews": {
            "eng": "THE ONE PIECE follows Monkey D. Luffy, an aspiring pirate who sets sail with his"
          },
          "aliases": null
        }
      ]
    }
    """;

    public const string NotFound = """{"status":"failure","data":null,"message":"NotFoundException: SeriesBase not found"}""";
}
