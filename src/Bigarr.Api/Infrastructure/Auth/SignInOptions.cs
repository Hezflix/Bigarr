namespace Bigarr.Api.Infrastructure.Auth;

/// <summary>Bound from <c>Bigarr:SignIn</c> (env: <c>Bigarr__SignIn__MaxFailuresPerUsername</c>…).</summary>
public sealed class SignInOptions
{
    public const string Section = "Bigarr:SignIn";

    /// <summary>Failed sign-ins on one username within <see cref="WindowMinutes"/> before it must wait. 0 turns the limit off.</summary>
    public int MaxFailuresPerUsername { get; set; } = 10;

    /// <summary>Failed sign-ins, or external sign-in starts, from one address within <see cref="WindowMinutes"/> before it must wait. 0 turns the limit off.</summary>
    public int MaxFailuresPerAddress { get; set; } = 30;

    public int WindowMinutes { get; set; } = 15;
}
