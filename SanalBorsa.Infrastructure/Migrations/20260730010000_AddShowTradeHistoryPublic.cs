using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SanalBorsa.Infrastructure.Data;

#nullable disable

namespace SanalBorsa.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260730010000_AddShowTradeHistoryPublic")]
public class AddShowTradeHistoryPublic : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "ShowTradeHistoryPublic",
            table: "Users",
            type: "bit",
            nullable: false,
            defaultValue: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ShowTradeHistoryPublic",
            table: "Users");
    }
}
