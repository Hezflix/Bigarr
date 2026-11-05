using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Import;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Infrastructure.Downloads.Mock;
using Bigarr.Api.Tests.Auth;
using Bigarr.Domain.History;
using Bigarr.Domain.Library;
using Bigarr.Domain.Movies;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Import;

/// <summary>Extra files (Radarr's "Import Extra Files"): subtitles come along with the movie, follow its renames and leave with it; files Bigarr did not import are never touched.</summary>
public sealed class ExtraFilesTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>, IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("bigarr-extras-lib-").FullName;
    private readonly string _bin = Directory.CreateTempSubdirectory("bigarr-extras-bin-").FullName;
    private readonly string _downloads = Directory.CreateTempSubdirectory("bigarr-extras-dl-").FullName;

    public void Dispose()
    {
        TryDelete(_root);
        TryDelete(_bin);
        TryDelete(_downloads);
    }

    [Theory]
    [InlineData("Movie.en.srt", "en", false, false)]
    [InlineData("Movie.eng.srt", "en", false, false)]
    [InlineData("English.srt", "en", false, false)]
    [InlineData("Subs/2_English.srt", "en", false, false)]
    [InlineData("Movie.fr.forced.srt", "fr", true, false)]
    [InlineData("Movie.en.sdh.srt", "en", false, true)]
    [InlineData("Subs/3_English.sdh.srt", "en", false, true)]
    [InlineData("Subs/4_French_forced.srt", "fr", true, false)]
    [InlineData("English (SDH).srt", "en", false, true)]
    [InlineData("Movie.forced.srt", null, true, false)]
    [InlineData("Movie.srt", null, false, false)]
    [InlineData("Movie.2010.1080p.BluRay.x264-SPARKS.srt", null, false, false)]
    public void A_subtitle_name_says_its_language_and_flags(string name, string? language, bool forced, bool sdh)
    {
        var described = ExtraFiles.Describe(Path.Combine(_downloads, name), "Movie");

        Assert.Equal(ExtraFileKind.Subtitle, described.Kind);
        Assert.Equal(language, described.Language);
        Assert.Equal(forced, described.Forced);
        Assert.Equal(sdh, described.HearingImpaired);
    }

    [Fact]
    public void A_title_that_reads_like_a_language_is_no_language()
    {
        // "It" is Italian's code: a subtitle named exactly like its video says nothing.
        Assert.Null(ExtraFiles.Describe(Path.Combine(_downloads, "It.srt"), "It").Language);
        Assert.Equal(ExtraFileKind.Other, ExtraFiles.Describe(Path.Combine(_downloads, "Movie.nfo"), "Movie").Kind);
    }

    [Fact]
    public void A_release_nfo_comes_once_as_nfo_orig_so_media_servers_do_not_read_it_as_metadata()
    {
        var release = Directory.CreateDirectory(Path.Combine(_downloads, "Nfo.Film.2005.1080p.BluRay.x264-GRP")).FullName;
        var video = Touch(release, "Nfo.Film.2005.1080p.BluRay.x264-GRP.mkv");
        Touch(release, Path.Combine("Extras", "grp.nfo"));
        Touch(release, "Nfo.Film.2005.1080p.BluRay.x264-GRP.nfo");
        Touch(release, "Nfo.Film.2005.1080p.BluRay.x264-GRP.pdf");

        var found = ExtraFiles.Find(video, release, ExtraFiles.ParseExtensions("srt,nfo,pdf"));

        // The .nfo named after the video wins over the other one; a .pdf is just another extra.
        Assert.Equal(["Nfo.Film.2005.1080p.BluRay.x264-GRP.nfo", "Nfo.Film.2005.1080p.BluRay.x264-GRP.pdf"], found.Select(c => c.Source.Name));
        Assert.Equal(["Movie.nfo-orig", "Movie.pdf"], found.Select(c => c.Name.For("Movie", 0)));
        Assert.All(found, c => Assert.Equal(ExtraFileKind.Other, c.Name.Kind));

        // A rename keeps the suffix: it is stored with the extra.
        var stored = new ExtraFile { RelativePath = "Old.nfo-orig", Extension = ".nfo-orig", Kind = ExtraFileKind.Other };
        Assert.Equal("New.nfo-orig", ExtraFileName.Of(stored).For("New", 0));
    }

    [Fact]
    public void A_folder_release_gives_everything_under_it_and_a_shared_folder_only_the_video_siblings()
    {
        var release = Directory.CreateDirectory(Path.Combine(_downloads, "Movie.2010.1080p.BluRay.x264-GRP")).FullName;
        var video = Touch(release, "Movie.2010.1080p.BluRay.x264-GRP.mkv");
        Touch(release, "Movie.2010.1080p.BluRay.x264-GRP-sample.mkv");
        Touch(release, "Movie.2010.1080p.BluRay.x264-GRP.en.srt");
        Touch(release, "Movie.2010.1080p.BluRay.x264-GRP.nfo");
        Touch(release, Path.Combine("Subs", "2_English.srt"));
        Touch(release, Path.Combine("Sample", "sample.en.srt"));
        var extensions = ExtraFiles.ParseExtensions(MediaManagementConfig.DefaultExtraFileExtensions);

        var found = ExtraFiles.Find(video, release, extensions).Select(c => Path.GetRelativePath(release, c.Source.FullName)).ToList();
        Assert.Equal(["Movie.2010.1080p.BluRay.x264-GRP.en.srt", Path.Combine("Subs", "2_English.srt")], found);

        // Two films side by side: only what is named after the picked video.
        var flat = Directory.CreateDirectory(Path.Combine(_downloads, "flat")).FullName;
        var first = Touch(flat, "First.Film.2001.mkv");
        Touch(flat, "First.Film.2001.en.srt");
        Touch(flat, "Second.Film.2002.mkv");
        Touch(flat, "Second.Film.2002.en.srt");
        Touch(flat, "First.Film.2001.Extended.mkv.srt.bak");
        Assert.Equal(["First.Film.2001.en.srt"], ExtraFiles.Find(first, releasePath: null, extensions).Select(c => c.Source.Name));

        // A single-file download never looks past its own name, even alone in its folder.
        var single = Directory.CreateDirectory(Path.Combine(_downloads, "single")).FullName;
        var lone = Touch(single, "Lone.Film.2003.mkv");
        Touch(single, "Lone.Film.2003.fr.srt");
        Touch(single, "unrelated.en.srt");
        Assert.Equal(["Lone.Film.2003.fr.srt"], ExtraFiles.Find(lone, releasePath: lone, extensions).Select(c => c.Source.Name));
    }

    [Fact]
    public void Destinations_are_numbered_around_taken_names_and_keep_vobsub_pairs_together()
    {
        var names = new[]
        {
            new ExtraFileName("/dl/a", ".idx", ExtraFileKind.Subtitle, "en", false, false),
            new ExtraFileName("/dl/a", ".sub", ExtraFileKind.Subtitle, "en", false, false),
            new ExtraFileName("/dl/b", ".srt", ExtraFileKind.Subtitle, "en", false, false),
            new ExtraFileName("/dl/c", ".srt", ExtraFileKind.Subtitle, "fr", true, true),
            new ExtraFileName("/dl/d", ".nfo", ExtraFileKind.Other, null, false, false),
        };
        var taken = new HashSet<string> { Path.Combine("/lib", "Movie.en.srt") };

        var paths = ExtraFiles.Destinations(names, "/lib", "Movie", taken.Contains).Select(Path.GetFileName).ToList();

        Assert.Equal(["Movie.en.idx", "Movie.en.sub", "Movie.1.en.srt", "Movie.fr.forced.sdh.srt", "Movie.nfo"], paths);
    }

    [Fact]
    public void The_extension_list_is_normalized_and_refuses_videos()
    {
        Assert.Equal("srt,ass,vtt", ExtraFiles.Normalize(" SRT, .ass;*.vtt srt ", out var error));
        Assert.Null(error);
        Assert.Equal(string.Empty, ExtraFiles.Normalize(string.Empty, out _));
        Assert.Null(ExtraFiles.Normalize("srt,mkv", out error));
        Assert.Contains("video", error, StringComparison.Ordinal);
        Assert.Null(ExtraFiles.Normalize("srt,s/rt", out error));
        Assert.NotNull(error);
    }

    [Fact]
    public async Task Extras_come_with_the_download_follow_the_rename_and_leave_with_the_upgrade_and_the_deletion()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var root = await AddRootAsync(client, Path.Combine(_root, "auto"));
        await ConfigureAsync(client, _bin);

        var movie = await AddMovieAsync(client, 603, hd, root.Id);
        var version = movie.Versions.Single();
        var folder = Path.Combine(root.Path, "The Matrix (1999)");
        var indexer = await AddMockIndexerAsync(client, "Mock extras idx");
        var downloadClient = await AddMockClientAsync(client, "Mock extras qbt");
        var state = factory.Services.GetRequiredService<MockDownloadClientState>();
        try
        {
            var search = await SearchAsync(client, movie.Id);

            // 1. The release ships two subtitles and an .nfo that is not in the list.
            var webdl = search.Releases.Single(r => r.Title.Contains("1080p.WEB-DL.DDP5.1.H.264-NTb", StringComparison.Ordinal));
            var grab = await GrabAsync(client, webdl, version.Id);
            var content = Prepare(state, grab.DownloadId, ".en.srt", ".fr.forced.srt", ".nfo");
            await RefreshAsync(client);
            await CompleteAsync(client, grab.DownloadId);
            await RefreshAsync(client);

            var imported = await WaitForFileAsync(client, movie.Id, version.Id);
            Assert.Equal("The Matrix (1999) [WEBDL-1080p].mkv", imported.File!.RelativePath);
            Assert.Collection(
                imported.File.Extras,
                en =>
                {
                    Assert.Equal("The Matrix (1999) [WEBDL-1080p].en.srt", en.RelativePath);
                    Assert.Equal("en", en.Language);
                    Assert.Equal(ExtraFileKind.Subtitle, en.Kind);
                    Assert.False(en.Forced);
                },
                fr =>
                {
                    Assert.Equal("The Matrix (1999) [WEBDL-1080p].fr.forced.srt", fr.RelativePath);
                    Assert.Equal("fr", fr.Language);
                    Assert.True(fr.Forced);
                });
            Assert.All(imported.File.Extras, e => Assert.True(File.Exists(e.Path)));
            Assert.False(Directory.EnumerateFiles(folder, "*.nfo").Any(), "an extension outside the list stays in the download");
            Assert.True(File.Exists(Path.Combine(content, Path.GetFileName(content) + ".en.srt")), "a hardlink keeps the torrent's subtitles in place");
            Assert.Equal("2", Assert.Single(await HistoryAsync(client, movie.Id, HistoryEventType.Imported)).Data["extras"]);

            // Bazarr writes its own subtitle next to the movie: Bigarr never renames nor deletes it.
            var bazarr = Path.Combine(folder, "The Matrix (1999) [WEBDL-1080p].de.srt");
            await File.WriteAllTextAsync(bazarr, "bazarr");

            // 2. A new template: the extras are renamed with their movie file, shown in the preview first.
            await ConfigureAsync(client, _bin, "{Movie Title} {(Release Year)} - {Quality Full}");
            var preview = Assert.Single((await client.GetFromJsonAsync<List<RenamePreviewResponse>>($"/api/v1/rename?movieId={movie.Id}", Json))!);
            Assert.True(preview.Changed);
            Assert.Equal(["The Matrix (1999) - WEBDL-1080p.en.srt", "The Matrix (1999) - WEBDL-1080p.fr.forced.srt"], preview.Extras.Select(e => e.NewRelativePath));

            var renamed = await client.PostAsJsonAsync("/api/v1/rename", new RenameRequest(movie.Id), Json);
            Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
            var afterRename = (await GetMovieAsync(client, movie.Id)).Versions.Single().File!;
            Assert.Equal("The Matrix (1999) - WEBDL-1080p.mkv", afterRename.RelativePath);
            Assert.Equal(["The Matrix (1999) - WEBDL-1080p.en.srt", "The Matrix (1999) - WEBDL-1080p.fr.forced.srt"], afterRename.Extras.Select(e => e.RelativePath));
            Assert.All(afterRename.Extras, e => Assert.True(File.Exists(e.Path)));
            Assert.False(File.Exists(Path.Combine(folder, "The Matrix (1999) [WEBDL-1080p].en.srt")));
            Assert.True(File.Exists(bazarr), "an untracked subtitle keeps its name");
            Assert.Equal("2", Assert.Single(await HistoryAsync(client, movie.Id, HistoryEventType.Renamed)).Data["extras"]);

            // 3. The upgrade: the old extras go to the recycle bin with the old file, the new release brings its own.
            var bluray = search.Releases.Single(r => r.Title.EndsWith("1080p.BluRay.x264-SPARKS", StringComparison.Ordinal) && !r.Title.Contains("REPACK", StringComparison.Ordinal));
            var upgradeGrab = await GrabAsync(client, bluray, version.Id);
            Prepare(state, upgradeGrab.DownloadId, ".eng.srt");
            await RefreshAsync(client);
            await CompleteAsync(client, upgradeGrab.DownloadId);
            await RefreshAsync(client);

            var upgraded = await WaitForFileAsync(client, movie.Id, version.Id, v => v.File!.Quality == "Bluray-1080p");
            var extra = Assert.Single(upgraded.File!.Extras);
            Assert.Equal("The Matrix (1999) - Bluray-1080p.en.srt", extra.RelativePath);
            Assert.True(File.Exists(Path.Combine(_bin, "The Matrix (1999)", "The Matrix (1999) - WEBDL-1080p.en.srt")), "the old extras go to the recycle bin");
            Assert.True(File.Exists(Path.Combine(_bin, "The Matrix (1999)", "The Matrix (1999) - WEBDL-1080p.fr.forced.srt")));
            Assert.False(File.Exists(Path.Combine(folder, "The Matrix (1999) - WEBDL-1080p.en.srt")));
            Assert.True(File.Exists(bazarr), "an untracked subtitle survives the upgrade");

            // 4. Deleting the file takes its extras along, nothing else.
            Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/v1/movies/{movie.Id}/versions/{version.Id}/file")).StatusCode);
            Assert.False(File.Exists(extra.Path));
            Assert.True(File.Exists(Path.Combine(_bin, "The Matrix (1999)", "The Matrix (1999) - Bluray-1080p.en.srt")));
            Assert.Equal([Path.GetFileName(bazarr)], Directory.GetFiles(folder).Select(Path.GetFileName));
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/downloadclients/{downloadClient.Id}");
            await client.DeleteAsync($"/api/v1/indexers/{indexer.Id}");
            await client.DeleteAsync($"/api/v1/movies/{movie.Id}");
        }
    }

    [Fact]
    public async Task The_manual_import_takes_the_extras_too_and_numbers_around_a_file_already_there()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var root = await AddRootAsync(client, Path.Combine(_root, "manual"));
        await ConfigureAsync(client, _bin);

        var movie = await AddMovieAsync(client, 27205, hd, root.Id); // Inception
        var version = movie.Versions.Single();
        try
        {
            var release = Directory.CreateDirectory(Path.Combine(_downloads, "Inception.2010.1080p.BluRay.x264-SPARKS")).FullName;
            var video = Touch(release, "Inception.2010.1080p.BluRay.x264-SPARKS.mkv", 64 * 1024);
            Touch(release, "Inception.2010.1080p.BluRay.x264-SPARKS.en.srt");
            Touch(release, Path.Combine("Subs", "2_English.srt"));
            Touch(release, Path.Combine("Subs", "3_French.srt"));
            Touch(release, Path.Combine("Sample", "sample.srt"));

            // Something (not Bigarr) already wrote the English subtitle's name in the library: it is left alone.
            var folder = Directory.CreateDirectory(Path.Combine(root.Path, "Inception (2010)")).FullName;
            var squatter = Path.Combine(folder, "Inception (2010) [Bluray-1080p].en.srt");
            await File.WriteAllTextAsync(squatter, "already here");

            var response = await client.PostAsJsonAsync("/api/v1/manualimport", new ManualImportRequest([new ManualImportItemRequest(video, version.Id)]), Json);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = Assert.Single((await response.Content.ReadFromJsonAsync<List<ManualImportResultResponse>>(Json))!);
            Assert.True(result.Imported, result.Reason);

            var file = (await GetMovieAsync(client, movie.Id)).Versions.Single().File!;
            Assert.Equal("Inception (2010) [Bluray-1080p].mkv", file.RelativePath);
            Assert.Equal(
                ["Inception (2010) [Bluray-1080p].1.en.srt", "Inception (2010) [Bluray-1080p].2.en.srt", "Inception (2010) [Bluray-1080p].fr.srt"],
                file.Extras.Select(e => e.RelativePath));
            Assert.Equal(["en", "en", "fr"], file.Extras.Select(e => e.Language));
            Assert.Equal("already here", await File.ReadAllTextAsync(squatter));
            Assert.Equal(5, Directory.GetFiles(folder).Length); // the movie, three extras, the squatter: no sample subtitle
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/movies/{movie.Id}");
        }
    }

    [Fact]
    public async Task The_manual_import_lists_the_extras_and_brings_only_the_ones_kept()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var root = await AddRootAsync(client, Path.Combine(_root, "picked"));
        await ConfigureAsync(client, _bin);

        var movie = await AddMovieAsync(client, 335984, hd, root.Id); // Blade Runner 2049
        var version = movie.Versions.Single();
        try
        {
            var release = Directory.CreateDirectory(Path.Combine(_downloads, "Blade.Runner.2049.2017.1080p.BluRay.x264-GRP")).FullName;
            var video = Touch(release, "Blade.Runner.2049.2017.1080p.BluRay.x264-GRP.mkv", 64 * 1024);
            Touch(release, "Blade.Runner.2049.2017.1080p.BluRay.x264-GRP.fr.srt");
            Touch(release, Path.Combine("Subs", "2_English.srt"));
            Touch(release, Path.Combine("Subs", "3_English.sdh.srt"));
            Touch(release, Path.Combine("Sample", "sample.srt"));
            Touch(release, "Blade.Runner.2049.2017.1080p.BluRay.x264-GRP.nfo");

            var listing = (await client.GetFromJsonAsync<ManualImportResponse>($"/api/v1/manualimport?path={Uri.EscapeDataString(release)}", Json))!;
            var row = Assert.Single(listing.Files);
            Assert.Equal(
                ["Blade.Runner.2049.2017.1080p.BluRay.x264-GRP.fr.srt", Path.Combine("Subs", "2_English.srt"), Path.Combine("Subs", "3_English.sdh.srt")],
                row.Extras.Select(e => e.RelativePath));
            Assert.Equal(["fr", "en", "en"], row.Extras.Select(e => e.Language));
            Assert.True(row.Extras[2].HearingImpaired);

            // The user drops the plain English subtitle and keeps the other two.
            var kept = new List<string> { row.Extras[0].Path, row.Extras[2].Path };
            var response = await client.PostAsJsonAsync("/api/v1/manualimport", new ManualImportRequest([new ManualImportItemRequest(row.Path, version.Id, ExtraPaths: kept)]), Json);
            Assert.True(Assert.Single((await response.Content.ReadFromJsonAsync<List<ManualImportResultResponse>>(Json))!).Imported);

            var file = (await GetMovieAsync(client, movie.Id)).Versions.Single().File!;
            Assert.Equal(
                ["Blade Runner 2049 (2017) [Bluray-1080p].en.sdh.srt", "Blade Runner 2049 (2017) [Bluray-1080p].fr.srt"],
                file.Extras.Select(e => e.RelativePath));
            Assert.True(File.Exists(Path.Combine(release, "Subs", "2_English.srt")), "a dropped extra stays where it was");
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/movies/{movie.Id}");
        }
    }

    [Fact]
    public async Task Extensions_added_to_the_list_come_along_and_the_nfo_lands_as_nfo_orig()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var root = await AddRootAsync(client, Path.Combine(_root, "nfo"));
        var configured = await client.PutAsJsonAsync("/api/v1/mediamanagement", Request(_bin, MediaManagementConfig.DefaultFileNameTemplate, true, "srt,nfo,pdf"), Json);
        Assert.Equal(HttpStatusCode.OK, configured.StatusCode);

        var movie = await AddMovieAsync(client, 78, hd, root.Id); // Blade Runner
        var version = movie.Versions.Single();
        try
        {
            var release = Directory.CreateDirectory(Path.Combine(_downloads, "Blade.Runner.1982.1080p.BluRay.x264-GRP")).FullName;
            var video = Touch(release, "Blade.Runner.1982.1080p.BluRay.x264-GRP.mkv", 64 * 1024);
            Touch(release, "Blade.Runner.1982.1080p.BluRay.x264-GRP.nfo");
            Touch(release, "Blade.Runner.1982.1080p.BluRay.x264-GRP.pdf");

            var response = await client.PostAsJsonAsync("/api/v1/manualimport", new ManualImportRequest([new ManualImportItemRequest(video, version.Id)]), Json);
            Assert.True(Assert.Single((await response.Content.ReadFromJsonAsync<List<ManualImportResultResponse>>(Json))!).Imported);

            var file = (await GetMovieAsync(client, movie.Id)).Versions.Single().File!;
            Assert.Equal(["Blade Runner (1982) [Bluray-1080p].nfo-orig", "Blade Runner (1982) [Bluray-1080p].pdf"], file.Extras.Select(e => e.RelativePath));
            Assert.Equal([".nfo-orig", ".pdf"], file.Extras.Select(e => e.Extension));
            Assert.All(file.Extras, e => Assert.Equal(ExtraFileKind.Other, e.Kind));
            Assert.All(file.Extras, e => Assert.True(File.Exists(e.Path)));
            Assert.False(File.Exists(Path.Combine(root.Path, "Blade Runner (1982)", "Blade Runner (1982) [Bluray-1080p].nfo")), "nothing a media server would read as metadata");
        }
        finally
        {
            await ConfigureAsync(client, _bin);
            await client.DeleteAsync($"/api/v1/movies/{movie.Id}");
        }
    }

    [Fact]
    public async Task The_setting_off_imports_no_extra_and_the_list_is_validated()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var root = await AddRootAsync(client, Path.Combine(_root, "off"));
        await ConfigureAsync(client, _bin, importExtras: false);

        var invalid = await client.PutAsJsonAsync("/api/v1/mediamanagement", Request(_bin, MediaManagementConfig.DefaultFileNameTemplate, true, "srt,mkv"), Json);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var settings = (await client.GetFromJsonAsync<MediaManagementResponse>("/api/v1/mediamanagement", Json))!;
        Assert.False(settings.ImportExtraFiles);
        Assert.Equal(MediaManagementConfig.DefaultExtraFileExtensions, settings.ExtraFileExtensions);

        var movie = await AddMovieAsync(client, 157336, hd, root.Id); // Interstellar
        var version = movie.Versions.Single();
        try
        {
            var release = Directory.CreateDirectory(Path.Combine(_downloads, "Interstellar.2014.1080p.BluRay.x264-SPARKS")).FullName;
            var video = Touch(release, "Interstellar.2014.1080p.BluRay.x264-SPARKS.mkv", 64 * 1024);
            Touch(release, "Interstellar.2014.1080p.BluRay.x264-SPARKS.en.srt");
            var listing = (await client.GetFromJsonAsync<ManualImportResponse>($"/api/v1/manualimport?path={Uri.EscapeDataString(release)}", Json))!;
            Assert.Empty(Assert.Single(listing.Files).Extras);

            var response = await client.PostAsJsonAsync("/api/v1/manualimport", new ManualImportRequest([new ManualImportItemRequest(video, version.Id)]), Json);
            Assert.True(Assert.Single((await response.Content.ReadFromJsonAsync<List<ManualImportResultResponse>>(Json))!).Imported);

            var file = (await GetMovieAsync(client, movie.Id)).Versions.Single().File!;
            Assert.Empty(file.Extras);
            Assert.Equal([file.RelativePath], Directory.GetFiles(Path.Combine(root.Path, "Interstellar (2014)")).Select(Path.GetFileName));
            Assert.False((await HistoryAsync(client, movie.Id, HistoryEventType.Imported)).Single().Data.ContainsKey("extras"));
        }
        finally
        {
            await ConfigureAsync(client, _bin);
            await client.DeleteAsync($"/api/v1/movies/{movie.Id}");
        }
    }

    /// <summary>Writes the torrent's content now (video and sample), plus side files named after the video, before the mock client completes it.</summary>
    private static string Prepare(MockDownloadClientState state, string downloadId, params string[] suffixes)
    {
        var torrent = state.All.Single(t => t.Hash == downloadId);
        state.Materialize(torrent);
        var content = state.ContentPath(torrent);
        foreach (var suffix in suffixes)
        {
            File.WriteAllText(Path.Combine(content, Path.GetFileName(content) + suffix), suffix);
        }

        return content;
    }

    private static string Touch(string folder, string relativePath, int bytes = 16)
    {
        var path = Path.Combine(folder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    private static MediaManagementRequest Request(string bin, string template, bool importExtras, string extensions) =>
        new(true, template, MediaManagementConfig.DefaultFolderNameTemplate, TransferMode.HardlinkOrCopy, bin, 7, importExtras, extensions);

    private static async Task ConfigureAsync(HttpClient client, string bin, string template = MediaManagementConfig.DefaultFileNameTemplate, bool importExtras = true)
    {
        var response = await client.PutAsJsonAsync("/api/v1/mediamanagement", Request(bin, template, importExtras, MediaManagementConfig.DefaultExtraFileExtensions), Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
