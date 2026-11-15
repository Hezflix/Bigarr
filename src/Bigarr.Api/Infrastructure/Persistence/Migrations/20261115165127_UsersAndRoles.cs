using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bigarr.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UsersAndRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // An install that already has its settings row starts with UserPermissions.Defaults(): request films and
            // series, report issues. Existing users (the admin) keep every permission through their role.
            migrationBuilder.AddColumn<DateTime>(
                name: "DisabledAt",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Permissions_AutoApproveFilms",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Permissions_AutoApproveSeries",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Permissions_ReportIssues",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Permissions_Request4K",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Permissions_RequestFilms",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Permissions_RequestSeries",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DefaultPermissions_AutoApproveFilms",
                table: "InstanceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DefaultPermissions_AutoApproveSeries",
                table: "InstanceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DefaultPermissions_ReportIssues",
                table: "InstanceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "DefaultPermissions_Request4K",
                table: "InstanceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DefaultPermissions_RequestFilms",
                table: "InstanceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "DefaultPermissions_RequestSeries",
                table: "InstanceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "PlexNewAccounts",
                table: "InstanceSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisabledAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Permissions_AutoApproveFilms",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Permissions_AutoApproveSeries",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Permissions_ReportIssues",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Permissions_Request4K",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Permissions_RequestFilms",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Permissions_RequestSeries",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DefaultPermissions_AutoApproveFilms",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "DefaultPermissions_AutoApproveSeries",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "DefaultPermissions_ReportIssues",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "DefaultPermissions_Request4K",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "DefaultPermissions_RequestFilms",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "DefaultPermissions_RequestSeries",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "PlexNewAccounts",
                table: "InstanceSettings");
        }
    }
}
