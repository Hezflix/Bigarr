using Bigarr.Api.Tests.Metadata;
using Bigarr.Decision;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Quality;
using Bigarr.Domain.Tv;
using Bigarr.Parsing;
using EpisodeParser = Bigarr.Parsing.Episodes.Parser;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Tests.Decision;

/// <summary>
/// Sonarr's and Radarr's limits on propers and repacks: RSS takes no proper for a file older than a week, and a repack
/// only replaces a file of its own release group.
/// </summary>
public sealed class ProperRepackTests
{
    private const long Gb = 1024L * 1024 * 1024;

    private static readonly DateTime Now = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    private static readonly ReleaseEvaluator Rss = ReleaseEvaluator.Rss(new FixedTimeProvider(new DateTimeOffset(Now)));

    private static readonly ReleaseEvaluator AutomaticSearch = ReleaseEvaluator.Automatic();

    private static readonly ReleaseEvaluator Interactive = ReleaseEvaluator.Interactive();

    private static QualityProfile Profile() => new()
    {
        Id = 1,
        Name = "HD",
        PreferredLanguage = "en",
        UpgradeAllowed = true,
        Cutoff = ParserQuality.WEBDL1080p.Id,
        Items =
        [
            new QualityProfileItem { QualityIds = [ParserQuality.Bluray720p.Id], Allowed = true },
            new QualityProfileItem { QualityIds = [ParserQuality.WEBDL1080p.Id], Allowed = true },
            new QualityProfileItem { QualityIds = [ParserQuality.Bluray1080p.Id], Allowed = true },
        ],
    };

    /// <summary>20 to 200 MB per minute for every quality.</summary>
    private static readonly Dictionary<int, QualityDefinition> Definitions = ParserQuality.DefaultQualityDefinitions.ToDictionary(
        d => d.Quality.Id,
        d => new QualityDefinition { Id = d.Quality.Id, Name = d.Quality.Name, Resolution = d.Quality.Resolution, MinSize = 20, MaxSize = 200, SeriesMinSize = 20, SeriesMaxSize = 200 });

    private static ExistingFile WebDl(int daysOld, string? group = "NTb", bool pick = false) =>
        new(ParserQuality.WEBDL1080p.Id, 1, 0, false) { DateAdded = Now.AddDays(-daysOld), ReleaseGroup = group, IsPick = pick };

    private static string Reasons(ReleaseDecision decision) => string.Join(" | ", decision.Rejections.Select(r => r.Reason));

    // Films.

    private static TargetContext Film(ExistingFile? file) =>
        new(new MovieVersion { Id = 7, Name = "1080p", QualityProfileId = 1, FolderName = "Night Train (2020)", Monitored = true }, Profile(), Definitions, file, 120, [], false);

    private static ReleaseCandidate FilmRelease(string title) =>
        new(new ReleaseInfo { Guid = title, Title = title, IndexerName = "Mock", Size = 8 * Gb, Seeders = 50 }, Parser.ParseMovieTitle(title)!, 1);

    [Fact]
    public void Rss_takes_a_film_proper_for_a_recent_file_but_not_for_an_old_one()
    {
        var proper = FilmRelease("Night.Train.2020.PROPER.1080p.WEB-DL.DDP5.1.H.264-NTb");

        var recent = Rss.Evaluate(proper, Film(WebDl(3)));
        Assert.True(recent.Approved, Reasons(recent));

        var old = Rss.Evaluate(proper, Film(WebDl(10)));
        Assert.Equal("Proper for an old file: the existing file dates from 2026-10-30, more than 7 days ago", Assert.Single(old.Rejections).Reason);

        // Searches, automatic or interactive, still take it, past the cutoff.
        Assert.True(AutomaticSearch.Evaluate(proper, Film(WebDl(10))).Approved);
        Assert.True(Interactive.Evaluate(proper, Film(WebDl(10))).Approved);

        // A pick stands in for the file from the time it was made; without a file there is nothing to protect.
        Assert.Equal("Proper for an old file: the pick dates from 2026-10-30, more than 7 days ago", Assert.Single(Rss.Evaluate(proper, Film(WebDl(10, pick: true))).Rejections).Reason);
        Assert.True(Rss.Evaluate(proper, Film(null)).Approved);

        // Seven days back from today's date (UTC) is still recent.
        Assert.True(Rss.Evaluate(proper, Film(WebDl(7))).Approved);
    }

    [Fact]
    public void A_film_repack_only_replaces_a_file_of_its_own_release_group()
    {
        var repack = FilmRelease("Night.Train.2020.REPACK.1080p.WEB-DL.DDP5.1.H.264-NTb");

        var same = Interactive.Evaluate(repack, Film(WebDl(3, "ntb")));
        Assert.True(same.Approved, Reasons(same));

        Assert.Equal("Repack from NTb, but the existing file is from FLUX", Assert.Single(Interactive.Evaluate(repack, Film(WebDl(3, "FLUX"))).Rejections).Reason);
        Assert.Equal("Repack, but the release group of the existing file is unknown", Assert.Single(Interactive.Evaluate(repack, Film(WebDl(3, null))).Rejections).Reason);
        Assert.Equal("Repack from NTb, but the pick is from FLUX", Assert.Single(AutomaticSearch.Evaluate(repack, Film(WebDl(3, "FLUX", pick: true))).Rejections).Reason);

        var noGroup = FilmRelease("Night.Train.2020.REPACK.1080p.WEB-DL.DDP5.1.H.264");
        Assert.Equal("Repack with an unknown release group: it cannot be matched to the existing file", Assert.Single(Interactive.Evaluate(noGroup, Film(WebDl(3))).Rejections).Reason);

        // Not a newer revision of the file in place (another quality): the rule stays out of the way.
        var lower = new ExistingFile(ParserQuality.Bluray720p.Id, 1, 0, false) { ReleaseGroup = "FLUX" };
        Assert.True(Interactive.Evaluate(repack, Film(lower)).Approved);
    }

    [Fact]
    public void An_old_repack_of_another_group_is_refused_twice_in_rss()
    {
        var repack = FilmRelease("Night.Train.2020.REPACK.1080p.WEB-DL.DDP5.1.H.264-NTb");

        var decision = Rss.Evaluate(repack, Film(WebDl(30, "FLUX")));

        Assert.Equal(
            ["Repack from NTb, but the existing file is from FLUX", "Repack for an old file: the existing file dates from 2026-10-10, more than 7 days ago"],
            decision.Rejections.Select(r => r.Reason));
    }

    // Series.

    private static SeriesVersion Version() => new() { Id = 1, Name = "v1", QualityProfileId = 1, FolderName = "Harbour Lights (2021)" };

    private static TargetContext Episodes(params ExistingFile?[] files)
    {
        var version = Version();
        var slots = files.Select((file, i) => new EpisodeSlot(
                new EpisodeTarget { Id = 10_000 + i, Version = version, VersionId = version.Id, EpisodeId = 400 + i + 1, Monitored = true },
                400 + i + 1,
                4,
                i + 1,
                Now.AddDays(-60),
                45,
                file,
                false)
        {
            FileEpisodeIds = file is null ? [] : [400 + i + 1],
        })
            .ToList();
        return new TargetContext(slots[0].Target, Profile(), Definitions, null, 45, [], false)
        {
            Episodes = new EpisodeCoverage(slots, false, true, 45, Now),
        };
    }

    private static ReleaseCandidate EpisodeRelease(string title, double sizeGb) =>
        new(new ReleaseInfo { Guid = title, Title = title, IndexerName = "Mock", Size = (long)(sizeGb * Gb), Seeders = 50 }, EpisodeParser.ParseTitle(title), 1);

    [Fact]
    public void Rss_takes_an_episode_proper_for_a_recent_file_but_not_for_an_old_one()
    {
        var proper = EpisodeRelease("Harbour.Lights.S04E01.PROPER.1080p.WEB-DL.DDP5.1.H.264-NTb", 2);

        var recent = Rss.Evaluate(proper, Episodes(WebDl(3)));
        Assert.True(recent.Approved, Reasons(recent));

        Assert.Equal("Proper for an old file: the existing file dates from 2026-10-30, more than 7 days ago", Assert.Single(Rss.Evaluate(proper, Episodes(WebDl(10))).Rejections).Reason);
        Assert.True(AutomaticSearch.Evaluate(proper, Episodes(WebDl(10))).Approved);
        Assert.True(Interactive.Evaluate(proper, Episodes(WebDl(10))).Approved);
    }

    [Fact]
    public void A_season_pack_proper_is_refused_in_rss_when_one_episode_file_is_old()
    {
        var pack = EpisodeRelease("Harbour.Lights.S04.PROPER.1080p.WEB-DL.DDP5.1.H.264-NTb", 15);

        var allRecent = Rss.Evaluate(pack, Episodes(WebDl(1), WebDl(2), WebDl(3), null));
        Assert.True(allRecent.Approved, Reasons(allRecent));

        var oneOld = Rss.Evaluate(pack, Episodes(WebDl(1), WebDl(90), WebDl(3), null));
        Assert.Equal("S04E02: Proper for an old file: the existing file dates from 2026-08-11, more than 7 days ago", Assert.Single(oneOld.Rejections).Reason);

        var severalOld = Rss.Evaluate(pack, Episodes(WebDl(400), WebDl(90), WebDl(3), null));
        Assert.Equal("S04E01: Proper for an old file: the existing file dates from 2025-10-05, more than 7 days ago (and 1 more)", Assert.Single(severalOld.Rejections).Reason);
    }

    [Fact]
    public void A_season_pack_repack_only_replaces_episode_files_of_its_own_release_group()
    {
        var pack = EpisodeRelease("Harbour.Lights.S04.REPACK.1080p.WEB-DL.DDP5.1.H.264-NTb", 15);

        var same = Interactive.Evaluate(pack, Episodes(WebDl(3, "NTb"), WebDl(3, "NTB"), null));
        Assert.True(same.Approved, Reasons(same));

        var other = Interactive.Evaluate(pack, Episodes(WebDl(3, "NTb"), WebDl(3, "FLUX"), WebDl(3, null)));
        Assert.Equal("S04E02: Repack from NTb, but the existing file is from FLUX (and 1 more)", Assert.Single(other.Rejections).Reason);

        // One episode: no label, like the upgrade reasons.
        var single = EpisodeRelease("Harbour.Lights.S04E01.REPACK.1080p.WEB-DL.DDP5.1.H.264-NTb", 2);
        Assert.Equal("Repack, but the release group of the existing file is unknown", Assert.Single(Interactive.Evaluate(single, Episodes(WebDl(3, null))).Rejections).Reason);
    }
}
