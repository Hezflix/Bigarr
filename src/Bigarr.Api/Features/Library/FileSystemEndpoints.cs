using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Api.Infrastructure.Files;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Library;

/// <summary>Folder browsing for the web UI's path picker: one listing per folder, the client filters it as the user types.</summary>
public static class FileSystemEndpoints
{
    public static IEndpointRouteBuilder MapFileSystem(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/filesystem").WithTags("Library").RequireAuthorization(AuthPolicies.Api);

        group.MapGet("/", Browse);

        return app;
    }

    /// <summary>A half-typed path lists its deepest existing ancestor, so the picker keeps suggesting while the name is being written.</summary>
    private static Results<Ok<FolderListing>, ProblemHttpResult> Browse(string? path, bool includeFiles = false)
    {
        var result = FileSystemBrowser.List(path, includeFiles, climb: true);
        return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
    }
}
