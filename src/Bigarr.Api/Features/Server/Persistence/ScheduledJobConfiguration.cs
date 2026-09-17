using Bigarr.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bigarr.Api.Features.Server.Persistence;

public sealed class ScheduledJobConfiguration : IEntityTypeConfiguration<ScheduledJob>
{
    public void Configure(EntityTypeBuilder<ScheduledJob> builder)
    {
        builder.ToTable("ScheduledJobs");
        builder.HasKey(j => j.Name);
        builder.Property(j => j.Name).HasMaxLength(64);
        builder.Property(j => j.LastSummary).HasMaxLength(1024);
        builder.Property(j => j.LastError).HasMaxLength(2048);
    }
}
