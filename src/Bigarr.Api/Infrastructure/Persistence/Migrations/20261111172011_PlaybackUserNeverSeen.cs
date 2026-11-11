using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlaybackUserNeverSeen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "LastSeenAt",
                table: "MediaServerUsers",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "TEXT");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to a date for everyone: the never seen get their first-seen date again.
            migrationBuilder.Sql("UPDATE \"MediaServerUsers\" SET \"LastSeenAt\" = \"FirstSeenAt\" WHERE \"LastSeenAt\" IS NULL;");

            migrationBuilder.AlterColumn<DateTime>(
                name: "LastSeenAt",
                table: "MediaServerUsers",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
