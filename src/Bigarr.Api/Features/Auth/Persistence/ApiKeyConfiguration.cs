using Bigarr.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Auth.Persistence;

public sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("ApiKeys");

        builder.Property(k => k.Name).HasMaxLength(128);
        builder.Property(k => k.KeyHash).HasMaxLength(64);
        builder.Property(k => k.KeyPrefix).HasMaxLength(16);
        builder.Property(k => k.CompatView)
            .HasConversion(v => v.ToString(), s => CompatView.Parse(s))
            .HasMaxLength(80);
        builder.Ignore(k => k.IsRevoked);

        builder.HasIndex(k => k.KeyHash).IsUnique();
    }
}
