using Bigarr.Domain.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Quality.Persistence;

public sealed class ProfileFormatScoreConfiguration : IEntityTypeConfiguration<ProfileFormatScore>
{
    public void Configure(EntityTypeBuilder<ProfileFormatScore> builder)
    {
        builder.ToTable("ProfileFormatScores");
        builder.HasKey(s => new { s.QualityProfileId, s.CustomFormatId });

        builder.HasOne(s => s.CustomFormat)
            .WithMany(cf => cf.Scores)
            .HasForeignKey(s => s.CustomFormatId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
