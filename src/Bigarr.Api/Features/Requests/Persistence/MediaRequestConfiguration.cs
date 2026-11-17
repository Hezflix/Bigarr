using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Requests.Persistence;

public sealed class MediaRequestConfiguration : IEntityTypeConfiguration<MediaRequest>
{
    public void Configure(EntityTypeBuilder<MediaRequest> builder)
    {
        builder.ToTable("MediaRequests");

        builder.Property(r => r.Title).HasMaxLength(512);
        builder.Property(r => r.PosterUrl).HasMaxLength(1024);
        builder.Property(r => r.Seasons).HasJsonConversion();
        builder.Property(r => r.AddOptions).HasJsonConversion();
        builder.Property(r => r.DeclineReason).HasMaxLength(1024);
        builder.Property(r => r.Error).HasMaxLength(2048);
        builder.Property(r => r.VersionName).HasMaxLength(64);
        builder.Property(r => r.ForwardedTo).HasMaxLength(128);
        builder.Property(r => r.ForwardError).HasMaxLength(2048);
        builder.Ignore(r => r.IsActive);

        // The duplicate check, the Discover badges and the quota read by title and by user.
        builder.HasIndex(r => new { r.MediaType, r.TmdbId });
        builder.HasIndex(r => new { r.RequestedById, r.CreatedAt });
        builder.HasIndex(r => r.Status);
        builder.Property(r => r.SourceRef).HasMaxLength(64);
        builder.HasIndex(r => r.SourceRef).IsUnique();

        builder.HasOne(r => r.RequestedBy).WithMany().HasForeignKey(r => r.RequestedById).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(r => r.DecidedBy).WithMany().HasForeignKey(r => r.DecidedById).OnDelete(DeleteBehavior.SetNull);
    }
}
