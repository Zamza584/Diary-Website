using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NoteKeeping.Migrations
{
    /// <inheritdoc />
    public partial class LegalPrivacyControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AgeConfirmed",
                table: "UserAccounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "AgeConfirmedAt",
                table: "UserAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "InternationalTransferAcknowledged",
                table: "UserAccounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "InternationalTransferAcknowledgedAt",
                table: "UserAccounts",
                type: "timestamp with time zone",
                nullable: true);

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

            migrationBuilder.AddColumn<bool>(
                name: "PrivacyPolicyAccepted",
                table: "UserAccounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PrivacyPolicyAcceptedAt",
                table: "UserAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TermsAccepted",
                table: "UserAccounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "TermsAcceptedAt",
                table: "UserAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CopyrightNotices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReporterName = table.Column<string>(type: "text", nullable: false),
                    ReporterEmail = table.Column<string>(type: "text", nullable: false),
                    CopyrightWork = table.Column<string>(type: "text", nullable: false),
                    InfringingUrl = table.Column<string>(type: "text", nullable: false),
                    Explanation = table.Column<string>(type: "text", nullable: false),
                    Signature = table.Column<string>(type: "text", nullable: false),
                    AccuracyDeclarationAccepted = table.Column<bool>(type: "boolean", nullable: false),
                    AuthorityDeclarationAccepted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CopyrightNotices", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CopyrightNotices");

            migrationBuilder.DropColumn(
                name: "AgeConfirmed",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "AgeConfirmedAt",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "InternationalTransferAcknowledged",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "InternationalTransferAcknowledgedAt",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "MarketingConsent",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "MarketingConsentAt",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "MarketingConsentWithdrawnAt",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "PrivacyPolicyAccepted",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "PrivacyPolicyAcceptedAt",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "TermsAccepted",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "TermsAcceptedAt",
                table: "UserAccounts");
        }
    }
}
