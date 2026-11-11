using Bigarr.Domain.Library;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Naming;

namespace Bigarr.Api.Tests.Domain;

/// <summary>Sonarr's naming tokens and multi-episode styles, rendered the way Sonarr's FileNameBuilder does.</summary>
public sealed class SeriesNamingTemplateTests
{
    private static readonly SeriesNamingContext Show = new("Breaking Bad", 2008, 81189, 1396, "tt0903747", "1080p")
    {
        Episodes = [new EpisodeNaming(1, 1, 1, new DateOnly(2008, 1, 20), "Pilot")],
        QualityName = "WEBDL-1080p",
        ReleaseGroup = "NTb",
        Languages = ["English"],
        MediaInfo = new MediaInfo { VideoCodec = "x264", AudioCodec = "EAC3", AudioChannels = 5.1, AudioLanguages = ["English"], Subtitles = ["English", "French"] },
        CustomFormats = ["WEB Tier 01"],
        OriginalReleaseTitle = "Breaking.Bad.S01E01.1080p.WEB-DL.DDP5.1.H.264-NTb",
        OriginalFileName = "breaking.bad.s01e01.1080p.web-dl-ntb",
    };

    private static readonly SeriesNamingContext Three = Show with
    {
        Episodes =
        [
            new EpisodeNaming(1, 1, 1, null, "Pilot"),
            new EpisodeNaming(1, 2, 2, null, "Cat's in the Bag..."),
            new EpisodeNaming(1, 3, 3, null, "...And the Bag's in the River"),
        ],
    };

    [Fact]
    public void Sonarr_defaults_render_like_sonarr()
    {
        Assert.Equal("Breaking Bad - S01E01 - Pilot WEBDL-1080p", SeriesNamingTemplate.RenderFile(MediaManagementConfig.DefaultStandardEpisodeTemplate, Show, MultiEpisodeStyle.PrefixedRange));
        Assert.Equal("Breaking Bad (2008)", SeriesNamingTemplate.RenderSeriesFolder(MediaManagementConfig.DefaultSeriesFolderTemplate, Show));
        Assert.Equal("Season 1", SeriesNamingTemplate.RenderSeasonFolder(MediaManagementConfig.DefaultSeasonFolderTemplate, "Specials", 1, Show));
        Assert.Equal("Season 01", SeriesNamingTemplate.RenderSeasonFolder("Season {season:00}", "Specials", 1, Show));
        Assert.Equal("Specials", SeriesNamingTemplate.RenderSeasonFolder(MediaManagementConfig.DefaultSeasonFolderTemplate, "Specials", 0, Show));
        Assert.Equal("Season 12", SeriesNamingTemplate.RenderSeasonFolder(MediaManagementConfig.DefaultSeasonFolderTemplate, "Specials", 12, Show));
    }

    [Theory]
    [InlineData(MultiEpisodeStyle.Extend, "Breaking Bad - S01E01-02-03 - Pilot + Cat's in the Bag... + ...And the Bag's in the River")]
    [InlineData(MultiEpisodeStyle.Duplicate, "Breaking Bad - S01E01 - S01E02 - S01E03 - Pilot + Cat's in the Bag... + ...And the Bag's in the River")]
    [InlineData(MultiEpisodeStyle.Repeat, "Breaking Bad - S01E01E02E03 - Pilot + Cat's in the Bag... + ...And the Bag's in the River")]
    [InlineData(MultiEpisodeStyle.Scene, "Breaking Bad - S01E01-E02-E03 - Pilot + Cat's in the Bag... + ...And the Bag's in the River")]
    [InlineData(MultiEpisodeStyle.Range, "Breaking Bad - S01E01-03 - Pilot + Cat's in the Bag... + ...And the Bag's in the River")]
    [InlineData(MultiEpisodeStyle.PrefixedRange, "Breaking Bad - S01E01-E03 - Pilot + Cat's in the Bag... + ...And the Bag's in the River")]
    public void Every_multi_episode_style(MultiEpisodeStyle style, string expected) =>
        Assert.Equal(expected, SeriesNamingTemplate.RenderFile("{Series Title} - S{season:00}E{episode:00} - {Episode Title}", Three, style));

    [Theory]
    [InlineData(MultiEpisodeStyle.Extend, "Show 1x01-02-03")]
    [InlineData(MultiEpisodeStyle.Repeat, "Show 1x01x02x03")]
    [InlineData(MultiEpisodeStyle.Scene, "Show 1x01-x02-x03")]
    [InlineData(MultiEpisodeStyle.PrefixedRange, "Show 1x01-x03")]
    public void An_x_marker_works_too(MultiEpisodeStyle style, string expected) =>
        Assert.Equal(expected, SeriesNamingTemplate.RenderFile("Show {season}x{episode:00}", Three, style));

    [Fact]
    public void Multi_part_titles_collapse_to_one()
    {
        var parts = Show with { Episodes = [new EpisodeNaming(2, 12, 18, null, "Phoenix (1)"), new EpisodeNaming(2, 13, 19, null, "Phoenix (2)")] };
        Assert.Equal("Breaking Bad - S02E12-E13 - Phoenix", SeriesNamingTemplate.RenderFile("{Series Title} - S{season:00}E{episode:00} - {Episode Title}", parts, MultiEpisodeStyle.PrefixedRange));
    }

    [Fact]
    public void Anime_absolute_numbering()
    {
        var anime = new SeriesNamingContext("Frieren: Beyond Journey's End", 2023, 424536, null, null, "1080p")
        {
            Episodes = [new EpisodeNaming(1, 5, 5, null, "Phantoms of the Dead")],
            QualityName = "WEBDL-1080p",
            ReleaseGroup = "SubsPlease",
        };
        const string template = "{Series TitleYear} - S{season:00}E{episode:00} - {absolute:000} - {Episode Title} [{Quality Full}]-{Release Group}";
        Assert.Equal("Frieren - Beyond Journey's End (2023) - S01E05 - 005 - Phantoms of the Dead [WEBDL-1080p]-SubsPlease", SeriesNamingTemplate.RenderFile(template, anime, MultiEpisodeStyle.PrefixedRange));

        var two = anime with { Episodes = [new EpisodeNaming(1, 5, 5, null, "A"), new EpisodeNaming(1, 6, 6, null, "B")] };
        Assert.Equal("Frieren - 005-006", SeriesNamingTemplate.RenderFile("Frieren - {absolute:000}", two, MultiEpisodeStyle.Extend));
        Assert.Equal("Frieren - 005-006", SeriesNamingTemplate.RenderFile("Frieren - {absolute:000}", two, MultiEpisodeStyle.Range));
    }

    [Fact]
    public void Daily_air_date()
    {
        var daily = new SeriesNamingContext("The Daily Show", 1996, 71256, null, null, "1080p")
        {
            Episodes = [new EpisodeNaming(2026, 120, null, new DateOnly(2026, 10, 27), "Guest Name")],
            QualityName = "WEBDL-1080p",
        };
        Assert.Equal("The Daily Show - 2026-10-27 - Guest Name WEBDL-1080p", SeriesNamingTemplate.RenderFile(MediaManagementConfig.DefaultDailyEpisodeTemplate, daily, MultiEpisodeStyle.PrefixedRange));
        Assert.Equal("The.Daily.Show.2026.10.27", SeriesNamingTemplate.RenderFile("{Series.Title}.{Air.Date}", daily, MultiEpisodeStyle.PrefixedRange));
    }

    [Fact]
    public void Series_and_id_tokens()
    {
        Assert.Equal("Breaking Bad (2008) {tvdb-81189}", SeriesNamingTemplate.RenderSeriesFolder("{Series TitleYear} {tvdb-{TvdbId}}", Show));
        Assert.Equal("Breaking Bad [tmdbid-1396]", SeriesNamingTemplate.RenderSeriesFolder("{Series Title} [tmdbid-{TmdbId}]", Show));
        Assert.Equal("Breaking Bad 2008 {imdb-tt0903747}", SeriesNamingTemplate.RenderSeriesFolder("{Series CleanTitle} {Year} {imdb-{ImdbId}}", Show));
        Assert.Equal("Breaking Bad", SeriesNamingTemplate.RenderSeriesFolder("{Series Title} {tmdb-{TmdbId}}", Show with { TmdbId = null }));
        Assert.Equal("Office, The (2005)", SeriesNamingTemplate.RenderSeriesFolder("{Series TitleThe} ({Series Year})", Show with { SeriesTitle = "The Office", Year = 2005 }));
        Assert.Equal("Doctor Who (2005)", SeriesNamingTemplate.RenderSeriesFolder("{Series TitleYear}", Show with { SeriesTitle = "Doctor Who (2005)", Year = 2005 }));
        Assert.Equal("Doctor Who", SeriesNamingTemplate.RenderSeriesFolder("{Series TitleWithoutYear}", Show with { SeriesTitle = "Doctor Who (2005)" }));
        Assert.Equal("Breaking Bad [1080p]", SeriesNamingTemplate.RenderSeriesFolder("{Series Title} [{Version}]", Show));
        Assert.Equal("breaking.bad", SeriesNamingTemplate.RenderSeriesFolder("{series.title}", Show));
    }

    [Fact]
    public void File_tokens_and_sonarr_quality_names()
    {
        const string template = "{Series Title} S{season:00}E{episode:00} {Episode CleanTitle} [{Quality Full}][{MediaInfo VideoCodec} {MediaInfo AudioCodec} {MediaInfo AudioChannels}]{MediaInfo SubtitleLanguages}{[Custom Formats]}{-Release Group}";
        Assert.Equal("Breaking Bad S01E01 Pilot [WEBDL-1080p][x264 EAC3 5.1][EN+FR][WEB Tier 01]-NTb", SeriesNamingTemplate.RenderFile(template, Show, MultiEpisodeStyle.PrefixedRange));

        var remux = Show with { QualityName = "Remux-1080p", RevisionVersion = 2, ReleaseGroup = null };
        Assert.Equal("Breaking Bad S01E01 Bluray-1080p Remux Proper", SeriesNamingTemplate.RenderFile("{Series Title} S{season:00}E{episode:00} {Quality Full}{-Release Group}", remux, MultiEpisodeStyle.PrefixedRange));
        Assert.Equal("Breaking.Bad.S01E01.1080p.WEB-DL.DDP5.1.H.264-NTb", SeriesNamingTemplate.RenderFile("{Original Title}", Show, MultiEpisodeStyle.PrefixedRange));
    }

    [Fact]
    public void A_name_too_long_shortens_the_episode_title()
    {
        var verbose = Show with { Episodes = [new EpisodeNaming(1, 1, 1, null, new string('x', 400))] };
        var name = SeriesNamingTemplate.RenderFile(MediaManagementConfig.DefaultStandardEpisodeTemplate, verbose, MultiEpisodeStyle.PrefixedRange);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(name + ".mkv") <= SeriesNamingTemplate.MaxFileNameBytes);
        Assert.StartsWith("Breaking Bad - S01E01 - xxx", name, StringComparison.Ordinal);
        Assert.EndsWith("... WEBDL-1080p", name, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{Series Title} - {Episode Title} {Quality Full}", SeriesTemplateKind.Standard, "must contain {season} and {episode}")]
    [InlineData("{Series Title} - S{season:00}E{episode:00}", SeriesTemplateKind.Standard, "{Quality Full}, {Quality Title} or {Version}")]
    [InlineData("{Series Title} - {Episode Title} {Quality Full}", SeriesTemplateKind.Daily, "{Air-Date}")]
    [InlineData("{Series Title} - {Episode Title} {Quality Full}", SeriesTemplateKind.Anime, "{absolute}")]
    [InlineData("{Series Title} {Quality Full}", SeriesTemplateKind.SeriesFolder, "depends on the file")]
    [InlineData("{Series Title} {season}", SeriesTemplateKind.SeriesFolder, "{season} cannot be used")]
    [InlineData("Saison", SeriesTemplateKind.SeasonFolder, "must contain {season}")]
    [InlineData("{Series Nonsense} S{season}E{episode} {Quality Full}", SeriesTemplateKind.Standard, "Unknown token")]
    public void Invalid_templates_are_refused(string template, SeriesTemplateKind kind, string error) =>
        Assert.Contains(SeriesNamingTemplate.Validate(template, kind), e => e.Contains(error, StringComparison.Ordinal));

    [Fact]
    public void Defaults_are_valid()
    {
        Assert.Empty(SeriesNamingTemplate.Validate(MediaManagementConfig.DefaultStandardEpisodeTemplate, SeriesTemplateKind.Standard));
        Assert.Empty(SeriesNamingTemplate.Validate(MediaManagementConfig.DefaultDailyEpisodeTemplate, SeriesTemplateKind.Daily));
        Assert.Empty(SeriesNamingTemplate.Validate(MediaManagementConfig.DefaultAnimeEpisodeTemplate, SeriesTemplateKind.Anime));
        Assert.Empty(SeriesNamingTemplate.Validate(MediaManagementConfig.DefaultSeriesFolderTemplate, SeriesTemplateKind.SeriesFolder));
        Assert.Empty(SeriesNamingTemplate.Validate(MediaManagementConfig.DefaultSeasonFolderTemplate, SeriesTemplateKind.SeasonFolder));
        Assert.Empty(SeriesNamingTemplate.Validate(MediaManagementConfig.DefaultSpecialsFolderName, SeriesTemplateKind.SpecialsFolder));
        Assert.Empty(SeriesNamingTemplate.Validate("{Series Title} - {absolute:000} - {Episode Title} [{Quality Full}]", SeriesTemplateKind.Anime));
    }
}
