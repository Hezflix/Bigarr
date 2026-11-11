namespace Bigarr.Api.Features.Series.Persistence;

using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class EpisodeExtraFileConfiguration : IEntityTypeConfiguration<EpisodeExtraFile>
{
    public void Configure(EntityTypeBuilder<EpisodeExtraFile> builder)
    {
        builder.ToTable("EpisodeExtraFiles");

        builder.Property(e => e.RelativePath).HasMaxLength(1024);
        builder.Property(e => e.Extension).HasMaxLength(32);
        builder.Property(e => e.Language).HasMaxLength(8);

        builder.HasOne(e => e.EpisodeFile)
            .WithMany(f => f.Extras)
            .HasForeignKey(e => e.EpisodeFileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
