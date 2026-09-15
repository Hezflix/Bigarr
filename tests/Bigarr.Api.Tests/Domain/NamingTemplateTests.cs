using Bigarr.Domain.Movies;
using Bigarr.Domain.Naming;

namespace Bigarr.Api.Tests.Domain;

public sealed class NamingTemplateTests
{
    private static readonly NamingContext Matrix = new("The Matrix", "The Matrix", 1999, 603, "tt0133093", "1080p")
    {
        QualityName = "Bluray-1080p",
        ReleaseGroup = "SPARKS",
        Languages = ["English"],
    };

    [Fact]
    public void Default_templates_render_like_radarr()
    {
        Assert.Equal("The Matrix (1999) [Bluray-1080p]", NamingTemplate.Render(MediaManagementConfigDefaults.File, Matrix));
        Assert.Equal("The Matrix (1999)", NamingTemplate.Render(MediaManagementConfigDefaults.Folder, Matrix));

        var cut = Matrix with { Edition = "Director's Cut", RevisionVersion = 2 };
        Assert.Equal("The Matrix (1999) {edition-Director's Cut} [Bluray-1080p Proper]", NamingTemplate.Render(MediaManagementConfigDefaults.File, cut));
    }

    [Fact]
    public void Optional_prefix_and_suffix_disappear_with_an_empty_value()
    {
        var noYear = Matrix with { Year = null, ReleaseGroup = null };
        Assert.Equal("The Matrix [Bluray-1080p]", NamingTemplate.Render("{Movie Title} {(Release Year)} {[Quality Full]}{-Release Group}", noYear));
        Assert.Equal("The Matrix (1999) [Bluray-1080p]-SPARKS", NamingTemplate.Render("{Movie Title} {(Release Year)} {[Quality Full]}{-Release Group}", Matrix));
    }

    [Fact]
    public void Token_case_and_separators_shape_the_value()
    {
        Assert.Equal("the.matrix.1999.bluray-1080p", NamingTemplate.Render("{movie.title}.{release year}.{quality.title}", Matrix));
        Assert.Equal("THE_MATRIX", NamingTemplate.Render("{MOVIE_TITLE}", Matrix));
        Assert.Equal("Matrix, The (1999)", NamingTemplate.Render("{Movie TitleThe} ({Release Year})", Matrix));
    }

    [Fact]
    public void Media_info_tokens_read_the_probe_and_stay_quiet_without_it()
    {
        var probed = Matrix with
        {
            MediaInfo = new MediaInfo { VideoCodec = "x265", VideoBitDepth = 10, VideoDynamicRange = "HDR", VideoDynamicRangeType = "DV HDR10", AudioCodec = "TrueHD Atmos", AudioChannels = 7.1, AudioLanguages = ["English", "French"], Subtitles = ["English"] },
            CustomFormats = ["Atmos", "HDR10"],
        };
        Assert.Equal(
            "The Matrix (1999) [Bluray-1080p][DV HDR10][TrueHD Atmos 7.1][x265][EN+FR][EN] Atmos HDR10 Atmos",
            NamingTemplate.Render("{Movie Title} ({Release Year}) {[Quality Full]}{[MediaInfo VideoDynamicRangeType]}{[MediaInfo AudioCodec} {MediaInfo AudioChannels]}{[MediaInfo VideoCodec]}{MediaInfo AudioLanguages}{MediaInfo SubtitleLanguages} {Custom Formats} {Custom Format:atmos}", probed));
        Assert.Equal("The Matrix (1999) [Bluray-1080p]", NamingTemplate.Render("{Movie Title} ({Release Year}) {[Quality Full]}{[MediaInfo VideoDynamicRangeType]}{[MediaInfo AudioCodec} {MediaInfo AudioChannels]}", Matrix));
    }

    [Fact]
    public void Invalid_characters_are_sanitized()
    {
        var colon = Matrix with { MovieTitle = "Mission: Impossible", Year = 1996 };
        Assert.Equal("Mission - Impossible (1996) [Bluray-1080p]", NamingTemplate.Render(MediaManagementConfigDefaults.File, colon));
    }

    [Fact]
    public void Validation_rejects_unknown_tokens_file_only_tokens_in_folders_and_missing_discriminants()
    {
        Assert.Empty(NamingTemplate.Validate(MediaManagementConfigDefaults.File, forFile: true));
        Assert.Empty(NamingTemplate.Validate(MediaManagementConfigDefaults.Folder, forFile: false));
        Assert.Empty(NamingTemplate.Validate("{Movie Title} ({Release Year}) [{Version}]", forFile: false));

        Assert.Contains(NamingTemplate.Validate("{Movie Title} {Nope}", forFile: false), e => e.Contains("Unknown token {Nope}"));
        Assert.Contains(NamingTemplate.Validate("{Movie Title} {Quality Full}", forFile: false), e => e.Contains("cannot be used in a folder name"));
        Assert.Contains(NamingTemplate.Validate("{Movie Title} ({Release Year})", forFile: true), e => e.Contains("must contain"));
        Assert.Contains(NamingTemplate.Validate("", forFile: true), e => e.Contains("empty"));
    }

    [Fact]
    public void Clean_title_follows_radarr()
    {
        Assert.Equal("Mission Impossible", NamingTemplate.CleanTitle("Mission: Impossible"));
        Assert.Equal("Fast and Furious", NamingTemplate.CleanTitle("Fast & Furious"));
        Assert.Equal("Whats Up Doc", NamingTemplate.CleanTitle("What's Up, Doc?"));
    }

    private static class MediaManagementConfigDefaults
    {
        public const string File = Bigarr.Domain.Library.MediaManagementConfig.DefaultFileNameTemplate;

        public const string Folder = Bigarr.Domain.Library.MediaManagementConfig.DefaultFolderNameTemplate;
    }
}
