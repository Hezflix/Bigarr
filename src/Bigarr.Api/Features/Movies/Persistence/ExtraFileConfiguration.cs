using Bigarr.Domain.Movies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Movies.Persistence;

public sealed class ExtraFileConfiguration : IEntityTypeConfiguration<ExtraFile>
{
    public void Configure(EntityTypeBuilder<ExtraFile> builder)
    {
        builder.ToTable("ExtraFiles");

        builder.Property(e => e.RelativePath).HasMaxLength(1024);
        builder.Property(e => e.Extension).HasMaxLength(32);
        builder.Property(e => e.Language).HasMaxLength(8);

        builder.HasOne(e => e.MediaFile)
            .WithMany(f => f.Extras)
            .HasForeignKey(e => e.MediaFileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
