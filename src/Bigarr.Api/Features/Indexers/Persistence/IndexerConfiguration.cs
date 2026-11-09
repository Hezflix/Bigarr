using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Indexers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Indexers.Persistence;

public sealed class IndexerConfiguration : IEntityTypeConfiguration<Indexer>
{
    public void Configure(EntityTypeBuilder<Indexer> builder)
    {
        builder.ToTable("Indexers");

        builder.Property(i => i.Name).HasMaxLength(IndexerService.MaxNameLength);
        builder.Property(i => i.BaseUrl).HasMaxLength(512);
        builder.Property(i => i.ApiPath).HasMaxLength(128);
        builder.Property(i => i.ApiKey).HasMaxLength(256);
        builder.Property(i => i.Categories).HasJsonConversion();
        builder.Property(i => i.TvCategories).HasJsonConversion();
        builder.Property(i => i.AnimeCategories).HasJsonConversion();
        builder.Ignore(i => i.Protocol);

        builder.HasIndex(i => i.Name).IsUnique();
        builder.HasIndex(i => i.ProwlarrId).IsUnique().HasFilter("\"ProwlarrId\" IS NOT NULL");

        builder.HasMany(i => i.Tags)
            .WithMany()
            .UsingEntity<IndexerTag>(
                right => right.HasOne(t => t.Tag).WithMany().HasForeignKey(t => t.TagId).OnDelete(DeleteBehavior.Cascade),
                left => left.HasOne(t => t.Indexer).WithMany().HasForeignKey(t => t.IndexerId).OnDelete(DeleteBehavior.Cascade),
                join =>
                {
                    join.ToTable("IndexerTags");
                    join.HasKey(t => new { t.IndexerId, t.TagId });
                });
    }
}
