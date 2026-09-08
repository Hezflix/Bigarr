using Bigarr.Domain.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Quality.Persistence;

public sealed class QualityDefinitionConfiguration : IEntityTypeConfiguration<QualityDefinition>
{
    public void Configure(EntityTypeBuilder<QualityDefinition> builder)
    {
        builder.ToTable("QualityDefinitions");

        // Ids are the parser's fixed quality ids, seeded, never generated.
        builder.Property(q => q.Id).ValueGeneratedNever();
        builder.Property(q => q.Name).HasMaxLength(64);
        builder.Property(q => q.Title).HasMaxLength(64);
        builder.HasIndex(q => q.Name).IsUnique();
    }
}
