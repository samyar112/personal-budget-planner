using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaidSyncState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastSyncedAt",
                table: "PlaidItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SyncCursor",
                table: "PlaidItems",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Mask",
                table: "Accounts",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Accounts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastSyncedAt",
                table: "PlaidItems");

            migrationBuilder.DropColumn(
                name: "SyncCursor",
                table: "PlaidItems");

            migrationBuilder.DropColumn(
                name: "Mask",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Accounts");
        }
    }
}
