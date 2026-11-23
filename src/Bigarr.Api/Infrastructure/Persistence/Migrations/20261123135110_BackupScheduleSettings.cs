using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackupScheduleSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BackupIntervalDays",
                table: "InstanceSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BackupRetentionDays",
                table: "InstanceSettings",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BackupIntervalDays",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "BackupRetentionDays",
                table: "InstanceSettings");
        }
    }
}
