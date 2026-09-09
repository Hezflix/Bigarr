using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Quality.Persistence;

public sealed class QualityProfileConfiguration : IEntityTypeConfiguration<QualityProfile>
{
    public void Configure(EntityTypeBuilder<QualityProfile> builder)
    {
        builder.ToTable("QualityProfiles");

        builder.Property(p => p.Name).HasMaxLength(128);
        builder.Property(p => p.Items).HasJsonConversion();
        builder.Property(p => p.PreferredLanguage).HasMaxLength(8);
        builder.HasIndex(p => p.Name).IsUnique();

        builder.HasMany(p => p.FormatScores)
            .WithOne(s => s.QualityProfile)
            .HasForeignKey(s => s.QualityProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
