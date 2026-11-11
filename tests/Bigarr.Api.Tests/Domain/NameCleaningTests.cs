using Bigarr.Domain.Library;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Naming;

namespace Bigarr.Api.Tests.Domain;

/// <summary>
/// "Replace Illegal Characters" and "Colon Replacement", checked against what Radarr's and Sonarr's own unit tests expect
/// (their <c>ColonReplacementFixture</c>, <c>CleanFilenameFixture</c>, <c>ReplaceCharacterFixure</c>,
/// <c>ReservedDeviceNameFixture</c>, <c>GetMovieFolderFixture</c>, <c>GetSeriesFolderFixture</c> and <c>GetSeasonFolderFixture</c>,
/// develop branch) plus the titles that matter to a library: a subtitle after a colon, a time, a title ending with a colon.
/// </summary>
public sealed class NameCleaningTests
{
    private static NameCleaning Colon(ColonReplacement colon, bool replace = true, string custom = "") => new(replace, colon, custom);

    private static NamingContext Film(string title, int? year = null) => new(title, title, year, 603, "tt0133093", "1080p") { QualityName = "Bluray-1080p" };

    private static string FilmFile(string title, ColonReplacement colon, string template = "{Movie Title}", bool replace = true, int? year = null) =>
        NamingTemplate.Render(template, Film(title, year), Colon(colon, replace));

    private static string FilmFolder(string title, ColonReplacement colon, string template = "{Movie Title}", bool replace = true, int? year = null) =>
        NamingTemplate.RenderFolder(template, Film(title, year), Colon(colon, replace));

    private static SeriesNamingContext Show(string title, string episodeTitle = "City Sushi") =>
        new(title, null, 75978, null, null, "1080p")
        {
            Episodes = [new EpisodeNaming(15, 6, 100, null, episodeTitle)],
            QualityName = "HDTV-720p",
            ReleaseGroup = "SonarrTest",
        };

    private static string EpisodeFile(string title, ColonReplacement colon, string template = "{Series Title}", bool replace = true, string custom = "") =>
        SeriesNamingTemplate.RenderFile(template, Show(title), MultiEpisodeStyle.PrefixedRange, ".mkv", Colon(colon, replace, custom));

    // ---- Radarr's ColonReplacementFixture --------------------------------------------------------------------------------

    [Theory]
    [InlineData("CSI: Vegas", ColonReplacement.Smart, "CSI - Vegas")]
    [InlineData("CSI: Vegas", ColonReplacement.Dash, "CSI- Vegas")]
    [InlineData("CSI: Vegas", ColonReplacement.Delete, "CSI Vegas")]
    [InlineData("CSI: Vegas", ColonReplacement.SpaceDash, "CSI - Vegas")]
    [InlineData("CSI: Vegas", ColonReplacement.SpaceDashSpace, "CSI - Vegas")]
    [InlineData("Movie:Title", ColonReplacement.Smart, "Movie-Title")]
    [InlineData("Movie:Title", ColonReplacement.Dash, "Movie-Title")]
    [InlineData("Movie:Title", ColonReplacement.Delete, "MovieTitle")]
    [InlineData("Movie:Title", ColonReplacement.SpaceDash, "Movie -Title")]
    [InlineData("Movie:Title", ColonReplacement.SpaceDashSpace, "Movie - Title")]
    public void Radarr_colon_fixture_on_a_film_file(string title, ColonReplacement colon, string expected) =>
        Assert.Equal(expected, FilmFile(title, colon));

    [Theory]
    [InlineData("CSI: Vegas", ColonReplacement.Smart, "CSI - Vegas")]
    [InlineData("CSI: Vegas", ColonReplacement.Dash, "CSI- Vegas")]
    [InlineData("CSI: Vegas", ColonReplacement.Delete, "CSI Vegas")]
    [InlineData("CSI: Vegas", ColonReplacement.SpaceDash, "CSI - Vegas")]
    [InlineData("CSI: Vegas", ColonReplacement.SpaceDashSpace, "CSI - Vegas")]
    [InlineData("Series:Title", ColonReplacement.Smart, "Series-Title")]
    [InlineData("Series:Title", ColonReplacement.Dash, "Series-Title")]
    [InlineData("Series:Title", ColonReplacement.Delete, "SeriesTitle")]
    [InlineData("Series:Title", ColonReplacement.SpaceDash, "Series -Title")]
    [InlineData("Series:Title", ColonReplacement.SpaceDashSpace, "Series - Title")]
    public void Sonarr_colon_fixture_on_an_episode_file(string title, ColonReplacement colon, string expected) =>
        Assert.Equal(expected, EpisodeFile(title, colon));

    [Theory]
    [InlineData("Series: Title", "꞉", "Series꞉ Title")]
    [InlineData("Series: Title", "∶", "Series∶ Title")]
    [InlineData("Series: Title", "", "Series Title")]
    [InlineData("Series: Title", " ~", "Series ~ Title")]
    public void Sonarr_custom_colon_replacement(string title, string custom, string expected) =>
        Assert.Equal(expected, EpisodeFile(title, ColonReplacement.Custom, custom: custom));

    [Fact]
    public void Smart_is_the_default_and_replaces_a_colon_followed_by_a_space_with_space_dash_space()
    {
        Assert.Equal(new NameCleaning(true, ColonReplacement.Smart, ""), NameCleaning.Default);
        Assert.Equal("CSI - Vegas", NamingTemplate.Render("{Movie Title}", Film("CSI: Vegas")));
        Assert.Equal("CSI - Vegas", SeriesNamingTemplate.RenderFile("{Series Title}", Show("CSI: Vegas"), MultiEpisodeStyle.PrefixedRange));
        Assert.Equal("CSI - Vegas", FolderNameBuilder.Sanitize("CSI: Vegas"));
    }

    [Fact]
    public void The_enum_keeps_radarr_and_sonarr_ids()
    {
        Assert.Equal([0, 1, 2, 3, 4, 5], new[] { ColonReplacement.Delete, ColonReplacement.Dash, ColonReplacement.SpaceDash, ColonReplacement.SpaceDashSpace, ColonReplacement.Smart, ColonReplacement.Custom }.Select(c => (int)c));
    }

    // ---- the titles of a library, in every mode --------------------------------------------------------------------------

    [Theory]
    [InlineData("Star Wars: Andor", ColonReplacement.Delete, "Star Wars Andor")]
    [InlineData("Star Wars: Andor", ColonReplacement.Dash, "Star Wars- Andor")]
    [InlineData("Star Wars: Andor", ColonReplacement.SpaceDash, "Star Wars - Andor")]
    [InlineData("Star Wars: Andor", ColonReplacement.SpaceDashSpace, "Star Wars - Andor")]
    [InlineData("Star Wars: Andor", ColonReplacement.Smart, "Star Wars - Andor")]
    [InlineData("Mission: Impossible – Dead Reckoning", ColonReplacement.Delete, "Mission Impossible – Dead Reckoning")]
    [InlineData("Mission: Impossible – Dead Reckoning", ColonReplacement.Dash, "Mission- Impossible – Dead Reckoning")]
    [InlineData("Mission: Impossible – Dead Reckoning", ColonReplacement.SpaceDash, "Mission - Impossible – Dead Reckoning")]
    [InlineData("Mission: Impossible – Dead Reckoning", ColonReplacement.SpaceDashSpace, "Mission - Impossible – Dead Reckoning")]
    [InlineData("Mission: Impossible – Dead Reckoning", ColonReplacement.Smart, "Mission - Impossible – Dead Reckoning")]
    [InlineData("Law & Order: SVU", ColonReplacement.Delete, "Law & Order SVU")]
    [InlineData("Law & Order: SVU", ColonReplacement.Dash, "Law & Order- SVU")]
    [InlineData("Law & Order: SVU", ColonReplacement.SpaceDash, "Law & Order - SVU")]
    [InlineData("Law & Order: SVU", ColonReplacement.SpaceDashSpace, "Law & Order - SVU")]
    [InlineData("Law & Order: SVU", ColonReplacement.Smart, "Law & Order - SVU")]
    [InlineData("Meet at 10:30", ColonReplacement.Delete, "Meet at 1030")]
    [InlineData("Meet at 10:30", ColonReplacement.Dash, "Meet at 10-30")]
    [InlineData("Meet at 10:30", ColonReplacement.SpaceDash, "Meet at 10 -30")]
    [InlineData("Meet at 10:30", ColonReplacement.SpaceDashSpace, "Meet at 10 - 30")]
    [InlineData("Meet at 10:30", ColonReplacement.Smart, "Meet at 10-30")]
    public void Every_colon_mode_on_a_film_file_and_folder_and_on_an_episode_file(string title, ColonReplacement colon, string expected)
    {
        Assert.Equal(expected, FilmFile(title, colon));
        Assert.Equal(expected, FilmFolder(title, colon));
        Assert.Equal(expected, EpisodeFile(title, colon));
        Assert.Equal(expected, SeriesNamingTemplate.RenderSeriesFolder("{Series Title}", Show(title), Colon(colon)));
        Assert.Equal(expected, FolderNameBuilder.Sanitize(title, Colon(colon)));
    }

    [Theory]
    [InlineData(ColonReplacement.Delete, "Mission", "Mission", "Mission (1996)")]
    [InlineData(ColonReplacement.Dash, "Mission", "Mission-", "Mission- (1996)")]
    [InlineData(ColonReplacement.SpaceDash, "Mission", "Mission -", "Mission - (1996)")]
    [InlineData(ColonReplacement.SpaceDashSpace, "Mission", "Mission -", "Mission - (1996)")]
    [InlineData(ColonReplacement.Smart, "Mission", "Mission-", "Mission- (1996)")]
    public void A_title_ending_with_a_colon_loses_its_trailing_separator_in_a_file_only(ColonReplacement colon, string file, string folder, string folderWithYear)
    {
        // A file name ends clean (Radarr cuts every trailing "- ._"); a folder name only loses its dots and spaces.
        Assert.Equal(file, FilmFile("Mission:", colon));
        Assert.Equal(folder, FilmFolder("Mission:", colon));
        Assert.Equal(folderWithYear, FilmFolder("Mission:", colon, "{Movie Title} ({Release Year})", year: 1996));
        Assert.Equal(file, EpisodeFile("Mission:", colon));
    }

    [Fact]
    public void A_colon_left_in_the_middle_of_a_file_name_is_kept_apart_by_the_title()
    {
        Assert.Equal("Mission- (1996) [Bluray-1080p]", FilmFile("Mission:", ColonReplacement.Dash, "{Movie Title} ({Release Year}) [{Quality Full}]", year: 1996));
        Assert.Equal("Mission (1996) [Bluray-1080p]", FilmFile("Mission:", ColonReplacement.Delete, "{Movie Title} ({Release Year}) [{Quality Full}]", year: 1996));
    }

    // ---- Replace Illegal Characters, on and off --------------------------------------------------------------------------

    // Sonarr's ReplaceCharacterFixure (Radarr's BadCharacters are the same eight).
    [Theory]
    [InlineData("CSI: Crime Scene Investigation", "CSI - Crime Scene Investigation", "CSI Crime Scene Investigation")]
    [InlineData("Code:Breaker", "Code-Breaker", "CodeBreaker")]
    [InlineData("Back Slash\\", "Back Slash+", "Back Slash")]
    [InlineData("Forward Slash/", "Forward Slash+", "Forward Slash")]
    [InlineData("Greater Than>", "Greater Than", "Greater Than")]
    [InlineData("Less Than<", "Less Than", "Less Than")]
    [InlineData("Question Mark?", "Question Mark!", "Question Mark")]
    [InlineData("Aster*sk", "Aster-sk", "Astersk")]
    [InlineData("Colon: Two Periods", "Colon - Two Periods", "Colon Two Periods")]
    [InlineData("Pipe|", "Pipe", "Pipe")]
    [InlineData("Quotes\"", "Quotes", "Quotes")]
    [InlineData("Face/Off", "Face+Off", "FaceOff")]
    [InlineData("What? Why*Not", "What! Why-Not", "What WhyNot")]
    public void Illegal_characters_are_replaced_when_on_and_removed_when_off(string title, string replaced, string removed)
    {
        Assert.Equal(replaced, FilmFile(title, ColonReplacement.Smart));
        Assert.Equal(removed, FilmFile(title, ColonReplacement.Smart, replace: false));
        Assert.Equal(replaced, EpisodeFile(title, ColonReplacement.Smart));
        Assert.Equal(removed, EpisodeFile(title, ColonReplacement.Smart, replace: false));
        Assert.Equal(replaced, FilmFolder(title, ColonReplacement.Smart));
        Assert.Equal(removed, FilmFolder(title, ColonReplacement.Smart, replace: false));
    }

    [Theory]
    [InlineData(ColonReplacement.Delete)]
    [InlineData(ColonReplacement.Dash)]
    [InlineData(ColonReplacement.SpaceDash)]
    [InlineData(ColonReplacement.SpaceDashSpace)]
    [InlineData(ColonReplacement.Smart)]
    [InlineData(ColonReplacement.Custom)]
    public void With_replacement_off_the_colon_is_removed_whatever_the_colon_setting_says(ColonReplacement colon)
    {
        Assert.Equal("Star Wars Andor", FilmFile("Star Wars: Andor", colon, replace: false));
        Assert.Equal("Star Wars Andor", EpisodeFile("Star Wars: Andor", colon, replace: false, custom: "~"));
    }

    [Fact]
    public void Illegal_characters_typed_in_a_template_are_cleaned_too()
    {
        // Radarr leaves them (its template validator only knows the OS's invalid path characters); a library on shared storage
        // cannot hold them, so Bigarr applies the same settings to the literal text of a template.
        Assert.Equal("The Matrix - Bluray-1080p", NamingTemplate.Render("{Movie Title}: {Quality Title}", NamingTemplate.Sample, Colon(ColonReplacement.Smart)));
        Assert.Equal("The Matrix- Bluray-1080p", NamingTemplate.Render("{Movie Title}: {Quality Title}", NamingTemplate.Sample, Colon(ColonReplacement.Dash)));
        Assert.Equal("The Matrix Bluray-1080p", NamingTemplate.Render("{Movie Title}? {Quality Title}", NamingTemplate.Sample, Colon(ColonReplacement.Smart, replace: false)));
    }

    // ---- Radarr's and Sonarr's CleanFilenameFixture ----------------------------------------------------------------------

    [Theory]
    [InlineData("Mission: Impossible - no [HDTV-720p]", "Mission - Impossible - no [HDTV-720p]")]
    [InlineData("Law & Order: Criminal Intent - S10E07 - Icarus [HDTV-720p]", "Law & Order - Criminal Intent - S10E07 - Icarus [HDTV-720p]")]
    [InlineData(".45 (2006)", "45 (2006)")]
    [InlineData(".hack s01e01", "hack s01e01")]
    [InlineData(" The Movie Title", "The Movie Title")]
    [InlineData("The Movie Title ", "The Movie Title")]
    [InlineData(" The Movie Title ", "The Movie Title")]
    public void Clean_follows_both_apps(string name, string expected) => Assert.Equal(expected, NameCleaner.Clean(name));

    // ---- folders: Radarr's GetMovieFolderFixture, Sonarr's GetSeriesFolderFixture and GetSeasonFolderFixture ---------------

    [Theory]
    [InlineData("Arrival", 2016, "{Movie Title} ({Release Year})", "Arrival (2016)")]
    [InlineData("The Big Short", 2015, "{Movie TitleThe} ({Release Year})", "Big Short, The (2015)")]
    [InlineData("The Big Short", 2015, "{Movie Title} ({Release Year})", "The Big Short (2015)")]
    [InlineData("Con Game", 2021, "{Movie.Title} ({Release Year})", "Con_Game (2021)")]
    [InlineData("Com1 Sat", 2021, "{Movie.Title} ({Release Year})", "Com1_Sat (2021)")]
    public void Radarr_movie_folders(string title, int year, string template, string expected)
    {
        Assert.Equal(expected, NamingTemplate.RenderFolder(template, Film(title, year)));
        Assert.Equal(expected, NamingTemplate.Render(template, Film(title, year)));
    }

    [Theory]
    [InlineData("30 Rock", "{Series Title}", "30 Rock")]
    [InlineData("30 Rock", "{Series.Title}", "30.Rock")]
    [InlineData("24/7 Road to the NHL Winter Classic", "{Series Title}", "24+7 Road to the NHL Winter Classic")]
    [InlineData("Venture Bros.", "{Series.Title}", "Venture.Bros")]
    [InlineData(".hack", "{Series.Title}", "hack")]
    [InlineData("30 Rock", ".{Series.Title}.", "30.Rock")]
    [InlineData("Con Game", "{Series.Title}", "Con_Game")]
    [InlineData("Com1 Sat", "{Series.Title}", "Com1_Sat")]
    public void Sonarr_series_folders(string title, string template, string expected) =>
        Assert.Equal(expected, SeriesNamingTemplate.RenderSeriesFolder(template, Show(title)));

    [Theory]
    [InlineData("Venture Bros.", "{Series.Title}.{season:00}", "Venture.Bros.01")]
    [InlineData("Venture Bros.", "{Series Title} Season {season:00}", "Venture Bros. Season 01")]
    [InlineData("Series Title?", "{Series Title} Season {season:00}", "Series Title! Season 01")]
    [InlineData("Con Game", "{Series.Title} - Season {Season:00}", "Con_Game - Season 01")]
    [InlineData("Com1 Sat", "{Series.Title} - Season {Season:00}", "Com1_Sat - Season 01")]
    [InlineData("Show", "Season {season:00} -", "Season 01")]
    public void Sonarr_season_folders(string title, string template, string expected) =>
        Assert.Equal(expected, SeriesNamingTemplate.RenderSeasonFolder(template, "Specials", 1, Show(title)));

    [Fact]
    public void A_season_folder_cuts_dashes_and_underscores_but_a_series_folder_does_not()
    {
        Assert.Equal("Show -", SeriesNamingTemplate.RenderSeriesFolder("{Series Title} -", Show("Show")));
        Assert.Equal("01", SeriesNamingTemplate.RenderSeasonFolder("_{season:00}_ -", "Specials", 1, Show("Show")));
    }

    [Fact]
    public void Series_episode_files_follow_sonarr_for_reserved_names_and_trailing_characters()
    {
        Assert.Equal("Con_Game - S15E06", SeriesNamingTemplate.RenderFile("{Series.Title} - S{Season:00}E{Episode:00}", Show("Con Game"), MultiEpisodeStyle.PrefixedRange));
        Assert.Equal("Show - S15E06", SeriesNamingTemplate.RenderFile("{Series Title} - S{season:00}E{episode:00} - {Episode Title}", Show("Show", episodeTitle: "?"), MultiEpisodeStyle.PrefixedRange));

        // Sonarr trims a title's trailing space, dot and question mark before the character rules: "Who Are You?" stays "Who Are You".
        Assert.Equal("Show - S15E06 - Who Are You", SeriesNamingTemplate.RenderFile("{Series Title} - S{season:00}E{episode:00} - {Episode Title}", Show("Show", episodeTitle: "Who Are You?"), MultiEpisodeStyle.PrefixedRange));
        Assert.Equal("Show - S15E06 - What If", SeriesNamingTemplate.RenderFile("{Series Title} - S{season:00}E{episode:00} - {Episode Title}", Show("Show", episodeTitle: "What If..."), MultiEpisodeStyle.PrefixedRange));
    }

    [Fact]
    public void Repeated_separators_merge_but_a_literal_ellipsis_in_an_episode_template_survives()
    {
        // As in both apps: "..." in a value shrinks to "." (their cleanup of repeated separators)...
        Assert.Equal("What If.!", FilmFile("What If...?", ColonReplacement.Smart));
        Assert.Equal("Show - Season", SeriesNamingTemplate.RenderFile("Show -- {Series Title}", Show("Season"), MultiEpisodeStyle.PrefixedRange));

        // ...while Sonarr keeps the "..." typed in an episode format ({ellipsis}).
        Assert.Equal("Show... - S15E06", SeriesNamingTemplate.RenderFile("{Series Title}... - S{season:00}E{episode:00}", Show("Show"), MultiEpisodeStyle.PrefixedRange));
    }

    [Fact]
    public void A_custom_colon_cannot_hold_an_illegal_character()
    {
        Assert.Empty(NameCleaning.ValidateCustomColon(null));
        Assert.Empty(NameCleaning.ValidateCustomColon(""));
        Assert.Empty(NameCleaning.ValidateCustomColon("∶"));
        Assert.Empty(NameCleaning.ValidateCustomColon("꞉"));
        foreach (var bad in new[] { ":", "\\", "/", "<", ">", "?", "*", "|", "\"", "a:b", "x\0" })
        {
            Assert.NotEmpty(NameCleaning.ValidateCustomColon(bad));
        }
    }

    [Fact]
    public void A_name_that_cleans_to_nothing_is_an_underscore()
    {
        Assert.Equal("_", NameCleaner.Sanitize(""));
        Assert.Equal("_", NameCleaner.Sanitize("<>|", Colon(ColonReplacement.Smart)));
        Assert.Equal("_", FilmFolder("...", ColonReplacement.Smart));
    }
}
