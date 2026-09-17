using Bigarr.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Server.Tasks;

public static class TasksEndpoints
{
    public static IEndpointRouteBuilder MapTasks(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/system/tasks").WithTags("System").RequireAuthorization(AuthPolicies.Api);

        group.MapGet("/", async Task<Ok<List<ScheduledTaskResponse>>> (TaskRunner runner, CancellationToken ct) =>
            TypedResults.Ok(await runner.ListAsync(ct)));

        // Runs the job now and waits for it: what the "run" button and the tests call.
        group.MapPost("/{name}/run", async Task<Results<Ok<TaskRunResponse>, ProblemHttpResult>> (string name, TaskRunner runner, CancellationToken ct) =>
        {
            var result = await runner.RunAsync(name, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        return app;
    }
}
