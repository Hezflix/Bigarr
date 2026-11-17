using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeasonGroupPicks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SeasonGroup",
                table: "SeriesShadowPicks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SeasonGroupRealigned",
                table: "SeriesShadowPicks",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SeasonGroup",
                table: "SeriesShadowPicks");

            migrationBuilder.DropColumn(
                name: "SeasonGroupRealigned",
                table: "SeriesShadowPicks");
        }
    }
}
