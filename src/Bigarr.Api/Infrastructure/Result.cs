using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Infrastructure;

/// <summary>A failed operation, ready to become an RFC 9457 problem response.</summary>
public sealed record Failure(int StatusCode, string Title, string? Detail = null)
{
    public ProblemHttpResult ToProblem() => TypedResults.Problem(title: Title, detail: Detail, statusCode: StatusCode);

    public static Failure NotFound(string what) => new(StatusCodes.Status404NotFound, $"{what} not found");

    public static Failure Conflict(string title, string? detail = null) => new(StatusCodes.Status409Conflict, title, detail);

    public static Failure Invalid(string title, string? detail = null) => new(StatusCodes.Status400BadRequest, title, detail);
}

/// <summary>Application services return this instead of throwing for expected failures.</summary>
public readonly struct Result<T>
{
    private Result(T? value, Failure? failure)
    {
        Value = value;
        Failure = failure;
    }

    public T? Value { get; }

    public Failure? Failure { get; }

    public bool IsSuccess => Failure is null;

    public static Result<T> Ok(T value) => new(value, null);

    public static implicit operator Result<T>(Failure failure) => new(default, failure);
}
