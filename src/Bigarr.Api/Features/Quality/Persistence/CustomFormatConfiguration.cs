using Bigarr.Domain.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Quality.Persistence;

public sealed class CustomFormatConfiguration : IEntityTypeConfiguration<CustomFormat>
{
    public void Configure(EntityTypeBuilder<CustomFormat> builder)
    {
        builder.ToTable("CustomFormats");

        builder.Property(cf => cf.Name).HasMaxLength(128);
        builder.Property(cf => cf.SpecificationsJson).HasColumnType("TEXT");
        builder.HasIndex(cf => cf.Name).IsUnique();
    }
}
