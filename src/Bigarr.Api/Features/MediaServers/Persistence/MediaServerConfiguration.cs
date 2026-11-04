using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.MediaServers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.MediaServers.Persistence;

public sealed class MediaServerConfiguration : IEntityTypeConfiguration<MediaServer>
{
    public void Configure(EntityTypeBuilder<MediaServer> builder)
    {
        builder.ToTable("MediaServers");

        builder.Property(s => s.Name).HasMaxLength(MediaServerService.MaxNameLength);
        builder.Property(s => s.Url).HasMaxLength(512);
        builder.Property(s => s.Token).HasMaxLength(512);
        builder.Property(s => s.LastError).HasMaxLength(2048);
        builder.Property(s => s.PathMappings).HasJsonConversion();
        builder.Ignore(s => s.BaseUrl);
        builder.Ignore(s => s.IsFailing);

        builder.HasIndex(s => s.Name).IsUnique();
    }
}
