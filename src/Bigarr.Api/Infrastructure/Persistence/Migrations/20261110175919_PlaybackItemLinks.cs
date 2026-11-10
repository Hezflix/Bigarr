using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlaybackItemLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LinkedByHand",
                table: "MediaServerItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LinkedVersionId",
                table: "MediaServerItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaServerItems_LinkedVersionId",
                table: "MediaServerItems",
                column: "LinkedVersionId");

            migrationBuilder.AddForeignKey(
                name: "FK_MediaServerItems_MovieVersions_LinkedVersionId",
                table: "MediaServerItems",
                column: "LinkedVersionId",
                principalTable: "MovieVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MediaServerItems_MovieVersions_LinkedVersionId",
                table: "MediaServerItems");

            migrationBuilder.DropIndex(
                name: "IX_MediaServerItems_LinkedVersionId",
                table: "MediaServerItems");

            migrationBuilder.DropColumn(
                name: "LinkedByHand",
                table: "MediaServerItems");

            migrationBuilder.DropColumn(
                name: "LinkedVersionId",
                table: "MediaServerItems");
        }
    }
}
