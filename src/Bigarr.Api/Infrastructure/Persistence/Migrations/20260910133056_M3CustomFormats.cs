using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M3CustomFormats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TrashId",
                table: "CustomFormats",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrashScoresJson",
                table: "CustomFormats",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomFormats_TrashId",
                table: "CustomFormats",
                column: "TrashId",
                unique: true,
                filter: "\"TrashId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CustomFormats_TrashId",
                table: "CustomFormats");

            migrationBuilder.DropColumn(
                name: "TrashId",
                table: "CustomFormats");

            migrationBuilder.DropColumn(
                name: "TrashScoresJson",
                table: "CustomFormats");
        }
    }
}
