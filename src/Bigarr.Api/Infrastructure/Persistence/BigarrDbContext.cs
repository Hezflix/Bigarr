using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Auth;
using Bigarr.Domain.History;
using Bigarr.Domain.Library;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Quality;
using Bigarr.Domain.Tags;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Infrastructure.Persistence;

/// <summary>
/// Single database. Entity configurations live in the slice that owns the entity
/// (<c>Features/*/Persistence</c>) and are discovered by assembly scan.
/// </summary>
public sealed class BigarrDbContext(DbContextOptions<BigarrDbContext> options) : DbContext(options)
{
    public DbSet<Movie> Movies => Set<Movie>();

    public DbSet<MovieExclusion> MovieExclusions => Set<MovieExclusion>();

    public DbSet<AcquisitionTarget> AcquisitionTargets => Set<AcquisitionTarget>();

    public DbSet<MovieVersion> MovieVersions => Set<MovieVersion>();

    public DbSet<MediaFile> MediaFiles => Set<MediaFile>();

    public DbSet<Grab> Grabs => Set<Grab>();

    public DbSet<GrabTarget> GrabTargets => Set<GrabTarget>();

    public DbSet<BlocklistEntry> Blocklist => Set<BlocklistEntry>();

    public DbSet<HistoryEvent> History => Set<HistoryEvent>();

    public DbSet<QualityDefinition> QualityDefinitions => Set<QualityDefinition>();

    public DbSet<QualityProfile> QualityProfiles => Set<QualityProfile>();

    public DbSet<CustomFormat> CustomFormats => Set<CustomFormat>();

    public DbSet<RootFolder> RootFolders => Set<RootFolder>();

    public DbSet<UnmappedFolder> UnmappedFolders => Set<UnmappedFolder>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<User> Users => Set<User>();

    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BigarrDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }
}
