using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Series.Persistence;

public sealed class SeriesOrderChangeConfiguration : IEntityTypeConfiguration<SeriesOrderChange>
{
    public void Configure(EntityTypeBuilder<SeriesOrderChange> builder)
    {
        builder.ToTable("SeriesOrderChanges");
        builder.Property(c => c.From).HasJsonConversion();
        builder.Property(c => c.To).HasJsonConversion();
        builder.Property(c => c.PreviousNumbers).HasJsonConversion();
        builder.Property(c => c.PreviousOrderOnly).HasJsonConversion();
        builder.Property(c => c.Moves).HasJsonConversion();
        builder.HasIndex(c => new { c.SeriesId, c.AppliedAt });

        builder.HasOne(c => c.Series)
            .WithMany()
            .HasForeignKey(c => c.SeriesId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
