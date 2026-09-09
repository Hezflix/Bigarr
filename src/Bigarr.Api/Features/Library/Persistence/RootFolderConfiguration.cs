using Bigarr.Domain.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Library.Persistence;

public sealed class RootFolderConfiguration : IEntityTypeConfiguration<RootFolder>
{
    public void Configure(EntityTypeBuilder<RootFolder> builder)
    {
        builder.ToTable("RootFolders");
        builder.Property(r => r.Path).HasMaxLength(1024);
        builder.HasIndex(r => r.Path).IsUnique();
    }
}
