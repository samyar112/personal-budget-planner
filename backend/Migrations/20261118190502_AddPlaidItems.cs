using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaidItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlaidAccountId",
                table: "Accounts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PlaidItemId",
                table: "Accounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlaidItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EncryptedAccessToken = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    InstitutionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    InstitutionName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaidItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaidItems_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_PlaidItemId",
                table: "Accounts",
                column: "PlaidItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_UserId_PlaidAccountId",
                table: "Accounts",
                columns: new[] { "UserId", "PlaidAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaidItems_ItemId",
                table: "PlaidItems",
                column: "ItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaidItems_UserId",
                table: "PlaidItems",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Accounts_PlaidItems_PlaidItemId",
                table: "Accounts",
                column: "PlaidItemId",
                principalTable: "PlaidItems",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Accounts_PlaidItems_PlaidItemId",
                table: "Accounts");

            migrationBuilder.DropTable(
                name: "PlaidItems");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_PlaidItemId",
                table: "Accounts");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_UserId_PlaidAccountId",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "PlaidAccountId",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "PlaidItemId",
                table: "Accounts");
        }
    }
}
