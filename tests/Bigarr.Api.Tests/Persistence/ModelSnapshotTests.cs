using Bigarr.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Tests.Persistence;

/// <summary>
/// The entity classes and the migrations describe the same schema. A property or a mapping changed without
/// `dotnet ef migrations add` compiles, then fails on the first query that needs the missing column. This test asks
/// the question directly, without applying the migrations (applying them would throw EF's own, less helpful error
/// first), so the failure says what to run.
/// </summary>
public sealed class ModelSnapshotTests
{
    [Fact]
    public void Every_model_change_has_its_migration()
    {
        var options = new DbContextOptionsBuilder<BigarrDbContext>().UseSqlite("Data Source=:memory:").Options;
        using var db = new BigarrDbContext(options);

        Assert.False(
            db.Database.HasPendingModelChanges(),
            "The model differs from the last migration's snapshot. Run: dotnet tool restore && dotnet ef migrations add <Name> --project src/Bigarr.Api --output-dir Infrastructure/Persistence/Migrations (after a rebase onto a main that gained a migration, delete yours and add it again).");
    }
}
