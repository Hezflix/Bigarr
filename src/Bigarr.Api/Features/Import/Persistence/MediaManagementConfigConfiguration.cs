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
        builder.Property(c => c.ExtraFileExtensions).HasMaxLength(255);
        builder.Property(c => c.SeriesFolderTemplate).HasMaxLength(1024).HasDefaultValue(MediaManagementConfig.DefaultSeriesFolderTemplate);
        builder.Property(c => c.SeasonFolderTemplate).HasMaxLength(1024).HasDefaultValue(MediaManagementConfig.DefaultSeasonFolderTemplate);
        builder.Property(c => c.SpecialsFolderName).HasMaxLength(255).HasDefaultValue(MediaManagementConfig.DefaultSpecialsFolderName);
        builder.Property(c => c.StandardEpisodeTemplate).HasMaxLength(1024).HasDefaultValue(MediaManagementConfig.DefaultStandardEpisodeTemplate);
        builder.Property(c => c.DailyEpisodeTemplate).HasMaxLength(1024).HasDefaultValue(MediaManagementConfig.DefaultDailyEpisodeTemplate);
        builder.Property(c => c.AnimeEpisodeTemplate).HasMaxLength(1024).HasDefaultValue(MediaManagementConfig.DefaultAnimeEpisodeTemplate);
    }
}
