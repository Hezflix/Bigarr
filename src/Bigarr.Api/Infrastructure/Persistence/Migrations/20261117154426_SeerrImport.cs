using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeerrImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceRef",
                table: "Users",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceRef",
                table: "MediaRequests",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceRef",
                table: "Issues",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceRef",
                table: "IssueComments",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_SourceRef",
                table: "Users",
                column: "SourceRef",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaRequests_SourceRef",
                table: "MediaRequests",
                column: "SourceRef",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Issues_SourceRef",
                table: "Issues",
                column: "SourceRef",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IssueComments_SourceRef",
                table: "IssueComments",
                column: "SourceRef",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_SourceRef",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_MediaRequests_SourceRef",
                table: "MediaRequests");

            migrationBuilder.DropIndex(
                name: "IX_Issues_SourceRef",
                table: "Issues");

            migrationBuilder.DropIndex(
                name: "IX_IssueComments_SourceRef",
                table: "IssueComments");

            migrationBuilder.DropColumn(
                name: "SourceRef",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SourceRef",
                table: "MediaRequests");

            migrationBuilder.DropColumn(
                name: "SourceRef",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "SourceRef",
                table: "IssueComments");
        }
    }
}
