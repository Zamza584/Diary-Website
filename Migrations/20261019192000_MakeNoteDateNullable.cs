using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NoteKeeping.Data;

#nullable disable

namespace NoteKeeping.Migrations
{
    [DbContext(typeof(NoteKeepingDbContext))]
    [Migration("20261019192000_MakeNoteDateNullable")]
    public partial class MakeNoteDateNullable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "Date",
                table: "Notes",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "TEXT");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE Notes SET Date = CURRENT_TIMESTAMP WHERE Date IS NULL;");
            migrationBuilder.AlterColumn<DateTime>(
                name: "Date",
                table: "Notes",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
