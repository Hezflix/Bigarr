using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Server.Health;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealth(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/health").WithTags("System").RequireAuthorization(AuthPolicies.Api);

        // The last check; runs one first when none happened yet (right after startup).
        group.MapGet("/", async Task<Ok<HealthResponse>> (HealthState state, TaskRunner runner, IHostApplicationLifetime lifetime) =>
        {
            if (state.CheckedAt is null)
            {
                await runner.RunAsync(HealthCheckJob.JobName, lifetime.ApplicationStopping);
            }

            return TypedResults.Ok(state.Snapshot());
        });

        group.MapPost("/check", async Task<Results<Ok<HealthResponse>, ProblemHttpResult>> (HealthState state, TaskRunner runner, IHostApplicationLifetime lifetime) =>
        {
            // As for a task run by hand: the check finishes whatever becomes of the request.
            var run = await runner.RunAsync(HealthCheckJob.JobName, lifetime.ApplicationStopping);
            return run.IsSuccess ? TypedResults.Ok(state.Snapshot()) : run.Failure!.ToProblem();
        });

        return app;
    }
}
