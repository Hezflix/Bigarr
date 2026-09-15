using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M5Import : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MediaManagement",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    RenameFiles = table.Column<bool>(type: "INTEGER", nullable: false),
                    FileNameTemplate = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    FolderNameTemplate = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    TransferMode = table.Column<int>(type: "INTEGER", nullable: false),
                    RecycleBinPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    RecycleBinRetentionDays = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaManagement", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaManagement");
        }
    }
}
