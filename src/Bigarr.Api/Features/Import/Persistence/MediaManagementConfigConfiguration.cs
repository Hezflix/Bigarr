using Bigarr.Domain.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Import.Persistence;

public sealed class MediaManagementConfigConfiguration : IEntityTypeConfiguration<MediaManagementConfig>
{
    public void Configure(EntityTypeBuilder<MediaManagementConfig> builder)
    {
        builder.ToTable("MediaManagement");

        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.FileNameTemplate).HasMaxLength(1024);
        builder.Property(c => c.FolderNameTemplate).HasMaxLength(1024);
        builder.Property(c => c.RecycleBinPath).HasMaxLength(1024);
    }
}
