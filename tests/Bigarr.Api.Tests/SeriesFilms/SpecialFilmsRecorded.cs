namespace Bigarr.Api.Tests.SeriesFilms;

/// <summary>
/// Captured TVDB v4 answers (November 2026) for specials that are films, trimmed to the fields Bigarr reads: Demon Slayer
/// (TVDB 348545) lists its films in season 0, Mugen Train flagged <c>isMovie</c> with a linked movie record whose remote
/// ids give TMDB 635302; "Sibling's Bond" (a compilation screened in cinemas, not flagged) and "First Night" (a long
/// recap special) are not flagged.
/// </summary>
public static class SpecialFilmsRecorded
{
    /// <summary><c>GET /v4/series/348545/extended?meta=translations&amp;short=true</c>, trimmed.</summary>
    public const string SeriesExtended348545 = """
{
  "status": "success",
  "data": {
    "id": 348545,
    "name": "鬼滅の刃",
    "slug": "demon-slayer-kimetsu-no-yaiba",
    "image": "https://artworks.thetvdb.com/banners/v4/series/348545/posters/60908d475f49a.jpg",
    "firstAired": "2019-04-06",
    "lastAired": "2024-06-30",
    "status": {
      "id": 2,
      "name": "Ended"
    },
    "originalCountry": "jpn",
    "originalLanguage": "jpn",
    "defaultSeasonType": 1,
    "averageRuntime": 26,
    "overview": "時は大正、日本。\r\n炭を売る心優しき少年・炭治郎は、ある日鬼に家族を皆殺しにされてしまう。\r\nさらに唯一生き残った妹の禰豆子は、鬼に変貌してしまった。\r\n絶望的な現実に打ちのめされる炭治郎だったが、妹を人間に戻し、家族を殺した鬼を討つため、”鬼狩り”の道を進む決意をする。\r\n人と鬼とが織りなす哀しき兄妹の物語が、今、始まる――！",
    "year": "2019",
    "airsTime": "23:15",
    "originalNetwork": {
      "name": "Tokyo MX",
      "country": "jpn"
    },
    "remoteIds": [
      {
        "id": "tt9335498",
        "type": 2,
        "sourceName": "IMDB"
      },
      {
        "id": "85937",
        "type": 12,
        "sourceName": "TheMovieDB.com"
      }
    ],
    "translations": {
      "nameTranslations": [
        {
          "language": "eng",
          "name": "Demon Slayer: Kimetsu no Yaiba"
        },
        {
          "language": "eng",
          "name": "Blade of Demon Destruction"
        },
        {
          "language": "eng",
          "name": "Demon Slayer"
        }
      ]
    }
  }
}
""";

    /// <summary><c>GET /v4/series/348545/episodes/default/eng?page=0</c>, three specials and one regular episode.</summary>
    public const string Episodes348545 = """
{
  "status": "success",
  "data": {
    "series": {
      "id": 348545
    },
    "episodes": [
      {
        "id": 7766030,
        "seriesId": 348545,
        "name": "Movie: Sibling's Bond",
        "aired": "2019-03-29",
        "runtime": 105,
        "image": "/banners/series/348545/episodes/5ecfba95ac40e.jpg",
        "isMovie": 0,
        "number": 1,
        "absoluteNumber": 0,
        "seasonNumber": 0
      },
      {
        "id": 7766161,
        "seriesId": 348545,
        "name": "Demon Slayer Movie: Mugen Train",
        "aired": "2020-10-16",
        "runtime": 117,
        "image": "/banners/v4/episode/7766161/screencap/60805986343c9.jpg",
        "isMovie": 1,
        "number": 2,
        "absoluteNumber": 0,
        "seasonNumber": 0,
        "airsAfterSeason": 1,
        "linkedMovie": 131963
      },
      {
        "id": 8863223,
        "seriesId": 348545,
        "name": "First Night: Brother and Sister's Bond",
        "aired": "2021-09-11",
        "runtime": 110,
        "image": "/banners/v4/episode/8863223/screencap/61a926e98c462.jpg",
        "isMovie": 0,
        "number": 7,
        "absoluteNumber": 0,
        "seasonNumber": 0
      },
      {
        "id": 7122169,
        "seriesId": 348545,
        "name": "Cruelty",
        "aired": "2019-04-06",
        "runtime": 24,
        "image": "/banners/series/348545/episodes/7122169.jpg",
        "isMovie": 0,
        "number": 1,
        "absoluteNumber": 1,
        "seasonNumber": 1
      }
    ]
  },
  "links": {
    "next": null,
    "total_items": 4,
    "page_size": 500
  }
}
""";

    /// <summary><c>GET /v4/movies/131963/extended?short=true</c> (Mugen Train), ids only.</summary>
    public const string Movie131963 = """
{
  "status": "success",
  "data": {
    "id": 131963,
    "name": "劇場版「鬼滅の刃」 無限列車編",
    "year": "2020",
    "runtime": 117,
    "remoteIds": [
      {
        "id": "tt11032374",
        "type": 2,
        "sourceName": "IMDB"
      },
      {
        "id": "635302",
        "type": 10,
        "sourceName": "TheMovieDB.com"
      }
    ]
  }
}
""";
}
