using Bigarr.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Server.Backup;

public static class BackupEndpoints
{
    public static IEndpointRouteBuilder MapBackups(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/system/backup").WithTags("System").RequireAuthorization(AuthPolicies.Api);

        group.MapGet("/", Ok<List<BackupResponse>> (BackupService backups) => TypedResults.Ok(backups.List()));

        group.MapPost("/", async Task<Created<BackupResponse>> (BackupService backups, CancellationToken ct) =>
        {
            var backup = await backups.CreateAsync(scheduled: false, ct);
            return TypedResults.Created($"/api/v1/system/backup/{backup.Name}", backup);
        });

        group.MapDelete("/{name}", Results<NoContent, ProblemHttpResult> (string name, BackupService backups) =>
        {
            var result = backups.Delete(name);
            return result.IsSuccess ? TypedResults.NoContent() : result.Failure!.ToProblem();
        });

        group.MapGet("/{name}/download", Results<PhysicalFileHttpResult, NotFound> (string name, BackupService backups) =>
        {
            var path = backups.Resolve(name);
            return path is null || !File.Exists(path) ? TypedResults.NotFound() : TypedResults.PhysicalFile(Path.GetFullPath(path), "application/zip", name);
        });

        // Stages the database and stops the process: in Docker the container comes back on the restored file.
        group.MapPost("/{name}/restore", async Task<Results<Ok<RestoreResponse>, ProblemHttpResult>> (string name, BackupService backups, CancellationToken ct) =>
        {
            var result = await backups.RestoreAsync(name, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        return app;
    }
}
