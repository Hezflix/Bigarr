using Bigarr.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Auth.Persistence;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.Property(u => u.Username).HasMaxLength(64);
        builder.Property(u => u.PasswordHash).HasMaxLength(512);
        builder.HasIndex(u => u.Username).IsUnique();

        builder.HasMany(u => u.ApiKeys)
            .WithOne(k => k.User)
            .HasForeignKey(k => k.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ExternalLoginConfiguration : IEntityTypeConfiguration<ExternalLogin>
{
    public void Configure(EntityTypeBuilder<ExternalLogin> builder)
    {
        builder.ToTable("ExternalLogins");

        builder.Property(l => l.Provider).HasMaxLength(32);
        builder.Property(l => l.ExternalId).HasMaxLength(128);
        builder.Property(l => l.ExternalUsername).HasMaxLength(256);
        builder.Property(l => l.ThumbUrl).HasMaxLength(1024);

        // An outside account signs in one user; a user has one account per provider.
        builder.HasIndex(l => new { l.Provider, l.ExternalId }).IsUnique();
        builder.HasIndex(l => new { l.UserId, l.Provider }).IsUnique();

        builder.HasOne(l => l.User)
            .WithMany()
            .HasForeignKey(l => l.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ExternalLoginProviderConfiguration : IEntityTypeConfiguration<ExternalLoginProvider>
{
    public void Configure(EntityTypeBuilder<ExternalLoginProvider> builder)
    {
        builder.ToTable("ExternalLoginProviders");

        builder.HasKey(p => p.Provider);
        builder.Property(p => p.Provider).HasMaxLength(32);
    }
}
