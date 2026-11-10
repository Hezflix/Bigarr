using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlaybackItemOriginalTitle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OriginalTitle",
                table: "MediaServerItems",
                type: "TEXT",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OriginalTitle",
                table: "MediaServerItems");
        }
    }
}
