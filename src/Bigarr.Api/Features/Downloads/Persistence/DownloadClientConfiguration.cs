using Bigarr.Domain.Downloads;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Downloads.Persistence;

public sealed class DownloadClientConfiguration : IEntityTypeConfiguration<DownloadClient>
{
    public void Configure(EntityTypeBuilder<DownloadClient> builder)
    {
        builder.ToTable("DownloadClients");

        builder.Property(c => c.Name).HasMaxLength(DownloadClientService.MaxNameLength);
        builder.Property(c => c.Host).HasMaxLength(255);
        builder.Property(c => c.UrlBase).HasMaxLength(128);
        builder.Property(c => c.Username).HasMaxLength(128);
        builder.Property(c => c.Password).HasMaxLength(256);
        builder.Property(c => c.Category).HasMaxLength(DownloadClientService.MaxCategoryLength);
        builder.Property(c => c.SeriesCategory).HasMaxLength(DownloadClientService.MaxCategoryLength).HasDefaultValue(DownloadClient.DefaultSeriesCategory);
        builder.Ignore(c => c.Protocol);
        builder.Ignore(c => c.BaseUrl);

        builder.HasIndex(c => c.Name).IsUnique();
    }
}
