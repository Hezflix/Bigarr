using System.Security.Cryptography;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Auth;

/// <summary>
/// First start: creates the single admin account. The password comes from <c>Bigarr:InitialAdminPassword</c>
/// when set, otherwise a random one is generated and logged once.
/// </summary>
public sealed class AdminSeeder(
    BigarrDbContext db,
    IPasswordHasher<User> hasher,
    IOptions<BigarrOptions> options,
    ILogger<AdminSeeder> logger) : IStartupTask
{
    public const string DefaultUsername = "admin";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(cancellationToken))
        {
            return;
        }

        var configured = options.Value.InitialAdminPassword;
        var password = string.IsNullOrEmpty(configured) ? GeneratePassword() : configured;

        var user = new User { Username = DefaultUsername, PasswordHash = string.Empty };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        if (string.IsNullOrEmpty(configured))
        {
            logger.LogWarning(
                "No user found: created '{Username}' with a generated password. Log in and change it now.{NewLine}    Initial admin password: {Password}",
                DefaultUsername,
                Environment.NewLine,
                password);
        }
        else
        {
            logger.LogInformation("No user found: created '{Username}' with the configured initial password.", DefaultUsername);
        }
    }

    private static string GeneratePassword() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
