using Bigarr.Domain.Discover;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Discover.Persistence;

public sealed class DiscoverSliderConfiguration : IEntityTypeConfiguration<DiscoverSlider>
{
    public void Configure(EntityTypeBuilder<DiscoverSlider> builder)
    {
        builder.ToTable("DiscoverSliders");

        builder.Property(s => s.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(s => s.Title).HasMaxLength(100);
        builder.Property(s => s.Value).HasMaxLength(200);
        builder.Ignore(s => s.IsBuiltIn);

        // The built-in rows, created with the table: a new install opens on a full Discover page.
        builder.HasData(DiscoverSlider.Defaults.Select((kind, i) => new DiscoverSlider { Id = i + 1, Kind = kind, Enabled = true, Position = i }));
    }
}
