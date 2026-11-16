using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Issues;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Issues.Persistence;

public sealed class IssueConfiguration : IEntityTypeConfiguration<Issue>
{
    public void Configure(EntityTypeBuilder<Issue> builder)
    {
        builder.ToTable("Issues");

        builder.Property(i => i.Title).HasMaxLength(512);
        builder.Property(i => i.PosterUrl).HasMaxLength(1024);
        builder.Property(i => i.EpisodeLabel).HasMaxLength(512);
        builder.Property(i => i.VersionName).HasMaxLength(64);
        builder.Property(i => i.Message).HasMaxLength(IssueService.MaxMessageLength);
        builder.Property(i => i.File).HasJsonConversion();

        // The list filters by status and reporter; the title pages and the library pages read a title's open issues.
        builder.HasIndex(i => new { i.Status, i.CreatedAt });
        builder.HasIndex(i => new { i.ReportedById, i.CreatedAt });
        builder.HasIndex(i => new { i.MediaType, i.TmdbId });
        builder.HasIndex(i => i.MovieId);
        builder.HasIndex(i => i.SeriesId);

        // The library ids are references, not foreign keys: an issue outlives its title, its version and its file.
        builder.HasOne(i => i.ReportedBy).WithMany().HasForeignKey(i => i.ReportedById).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(i => i.ResolvedBy).WithMany().HasForeignKey(i => i.ResolvedById).OnDelete(DeleteBehavior.SetNull);
        builder.HasMany(i => i.Comments).WithOne(c => c.Issue).HasForeignKey(c => c.IssueId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class IssueCommentConfiguration : IEntityTypeConfiguration<IssueComment>
{
    public void Configure(EntityTypeBuilder<IssueComment> builder)
    {
        builder.ToTable("IssueComments");

        builder.Property(c => c.Message).HasMaxLength(IssueService.MaxMessageLength);
        builder.Property(c => c.Outcome).HasMaxLength(512);
        builder.Property(c => c.Detail).HasMaxLength(4096);
        builder.HasIndex(c => new { c.IssueId, c.CreatedAt });

        builder.HasOne(c => c.Author).WithMany().HasForeignKey(c => c.AuthorId).OnDelete(DeleteBehavior.SetNull);
    }
}
