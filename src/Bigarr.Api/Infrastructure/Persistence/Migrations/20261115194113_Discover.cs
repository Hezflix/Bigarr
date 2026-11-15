using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Discover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DiscoverFourKVersion",
                table: "InstanceSettings",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            // An existing install shows "4k" next to the default version, like a new one (the setting's default).
            migrationBuilder.Sql("UPDATE \"InstanceSettings\" SET \"DiscoverFourKVersion\" = '4k';");

            migrationBuilder.CreateTable(
                name: "DiscoverSliders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Value = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoverSliders", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "DiscoverSliders",
                columns: new[] { "Id", "Enabled", "Kind", "Position", "Title", "Value" },
                values: new object[,]
                {
                    { 1, true, "Trending", 0, null, null },
                    { 2, true, "PopularMovies", 1, null, null },
                    { 3, true, "MovieGenres", 2, null, null },
                    { 4, true, "UpcomingMovies", 3, null, null },
                    { 5, true, "Studios", 4, null, null },
                    { 6, true, "PopularSeries", 5, null, null },
                    { 7, true, "SeriesGenres", 6, null, null },
                    { 8, true, "UpcomingSeries", 7, null, null },
                    { 9, true, "Networks", 8, null, null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiscoverSliders");

            migrationBuilder.DropColumn(
                name: "DiscoverFourKVersion",
                table: "InstanceSettings");
        }
    }
}
