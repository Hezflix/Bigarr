using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Shadow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Shadow.Persistence;

public sealed class FollowedRadarrConfiguration : IEntityTypeConfiguration<FollowedRadarr>
{
    public void Configure(EntityTypeBuilder<FollowedRadarr> builder)
    {
        builder.ToTable("FollowedRadarrs");

        builder.Property(r => r.Name).HasMaxLength(64);
        builder.Property(r => r.Url).HasMaxLength(512);
        builder.Property(r => r.ApiKey).HasMaxLength(128);
        builder.Property(r => r.VersionName).HasMaxLength(64);
        builder.Property(r => r.ProfileMap).HasJsonConversion();
        builder.Property(r => r.RootFolderMap).HasJsonConversion();
        builder.Property(r => r.LastSummary).HasMaxLength(1024);
        builder.Property(r => r.LastError).HasMaxLength(2048);
        builder.Property(r => r.LastWebhookEvent).HasMaxLength(64);

        builder.HasIndex(r => r.Name).IsUnique();
    }
}
