using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Series.Persistence;

public sealed class SceneNameConfiguration : IEntityTypeConfiguration<SceneName>
{
    public void Configure(EntityTypeBuilder<SceneName> builder)
    {
        builder.ToTable("SceneNames");
        builder.Property(s => s.Title).HasMaxLength(500);
        builder.Property(s => s.CleanTitle).HasMaxLength(500);
        builder.HasIndex(s => s.CleanTitle);
        builder.HasIndex(s => s.TvdbId);
    }
}
