using Bigarr.Domain.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Library.Persistence;

public sealed class UnmappedFolderConfiguration : IEntityTypeConfiguration<UnmappedFolder>
{
    public void Configure(EntityTypeBuilder<UnmappedFolder> builder)
    {
        builder.ToTable("UnmappedFolders");
        builder.Property(u => u.RelativePath).HasMaxLength(255);
        builder.Property(u => u.Detail).HasMaxLength(2000);
        builder.HasIndex(u => new { u.RootFolderId, u.RelativePath }).IsUnique();

        builder.HasOne(u => u.RootFolder)
            .WithMany()
            .HasForeignKey(u => u.RootFolderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
