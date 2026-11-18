using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoteKeeping.Migrations
{
    /// <inheritdoc />
    public partial class RemoveMarketingPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MarketingConsent",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "MarketingConsentAt",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "MarketingConsentWithdrawnAt",
                table: "UserAccounts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MarketingConsent",
                table: "UserAccounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "MarketingConsentAt",
                table: "UserAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MarketingConsentWithdrawnAt",
                table: "UserAccounts",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}
