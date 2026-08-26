using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoteKeeping.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAccountIdToNotebook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UserAccountId",
                table: "Notebooks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Notebooks_UserAccountId",
                table: "Notebooks",
                column: "UserAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_Notebooks_UserAccounts_UserAccountId",
                table: "Notebooks",
                column: "UserAccountId",
                principalTable: "UserAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notebooks_UserAccounts_UserAccountId",
                table: "Notebooks");

            migrationBuilder.DropIndex(
                name: "IX_Notebooks_UserAccountId",
                table: "Notebooks");

            migrationBuilder.DropColumn(
                name: "UserAccountId",
                table: "Notebooks");
        }
    }
}
