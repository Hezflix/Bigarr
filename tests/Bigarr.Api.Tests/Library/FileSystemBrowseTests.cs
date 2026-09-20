using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Infrastructure.Files;
using Bigarr.Api.Tests.Auth;

namespace Bigarr.Api.Tests.Library;

/// <summary>The listing behind the path picker: what a folder holds, and what a half-typed path answers.</summary>
public sealed class FileSystemBrowseTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>, IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("bigarr-browse-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }

    [Fact]
    public async Task Browsing_requires_a_session()
    {
        var response = await factory.CreateAnonymousClient().GetAsync(new Uri("/api/v1/filesystem", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_folder_lists_its_visible_subfolders_and_keeps_files_out_unless_asked()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Movies"));
        Directory.CreateDirectory(Path.Combine(_root, "Series"));
        Directory.CreateDirectory(Path.Combine(_root, ".cache"));
        await File.WriteAllTextAsync(Path.Combine(_root, "note.txt"), "x");

        var client = await ClientAsync();

        var listing = await BrowseAsync(client, _root);
        Assert.True(listing.Exists);
        Assert.Equal(_root, listing.Path);
        Assert.Equal(Directory.GetParent(_root)!.FullName, listing.Parent);
        Assert.Equal(["Movies", "Series"], listing.Directories.Select(d => d.Name));
        Assert.Equal(Path.Combine(_root, "Movies"), listing.Directories[0].Path);
        Assert.Empty(listing.Files);

        var withFiles = await BrowseAsync(client, _root, includeFiles: true);
        Assert.Equal(["note.txt"], withFiles.Files.Select(f => f.Name));
    }

    [Fact]
    public async Task A_half_typed_path_lists_the_deepest_folder_that_exists()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Movies"));
        var client = await ClientAsync();

        // What the picker asks for after "…/Mov": the folder to filter, plus the news that the path is not one yet.
        var typing = await BrowseAsync(client, Path.Combine(_root, "Mov"));
        Assert.False(typing.Exists);
        Assert.Equal(_root, typing.Path);
        Assert.Equal(["Movies"], typing.Directories.Select(d => d.Name));

        var missing = await BrowseAsync(client, Path.Combine(_root, "Nope", "Deeper", "Still"));
        Assert.False(missing.Exists);
        Assert.Equal(_root, missing.Path);
    }

    [Fact]
    public async Task Without_a_path_the_mount_points_come_back()
    {
        var client = await ClientAsync();

        var listing = await BrowseAsync(client, path: null);

        Assert.Equal(string.Empty, listing.Path);
        Assert.Null(listing.Parent);
        Assert.False(listing.Exists);
        Assert.NotEmpty(listing.Directories);
        Assert.All(listing.Directories, d => Assert.True(Path.IsPathRooted(d.Path)));
    }

    private static async Task<FolderListing> BrowseAsync(HttpClient client, string? path, bool includeFiles = false)
    {
        var query = path is null ? string.Empty : $"path={Uri.EscapeDataString(path)}&";
        var response = await client.GetAsync(new Uri($"/api/v1/filesystem?{query}includeFiles={includeFiles}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<FolderListing>())!;
    }

    private async Task<HttpClient> ClientAsync()
    {
        var client = factory.CreateClientWithCookies();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = BigarrApiFactory.AdminUsername, password = BigarrApiFactory.AdminPassword });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }
}
