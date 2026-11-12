using Bigarr.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Server.Persistence;

public sealed class StandingHealthIssueConfiguration : IEntityTypeConfiguration<StandingHealthIssue>
{
    public void Configure(EntityTypeBuilder<StandingHealthIssue> builder)
    {
        builder.ToTable("StandingHealthIssues");
        builder.Property(i => i.Source).HasMaxLength(64);
        builder.Property(i => i.Message).HasMaxLength(2048);
    }
}
