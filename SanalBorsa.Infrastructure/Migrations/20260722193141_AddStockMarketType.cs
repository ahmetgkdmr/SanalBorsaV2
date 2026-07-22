using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SanalBorsa.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStockMarketType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Stocks_Symbol",
                table: "Stocks");

            migrationBuilder.AlterColumn<string>(
                name: "YahooSymbol",
                table: "Stocks",
                type: "varchar(32)",
                unicode: false,
                maxLength: 32,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "varchar(25)",
                oldUnicode: false,
                oldMaxLength: 25);

            migrationBuilder.AlterColumn<string>(
                name: "Symbol",
                table: "Stocks",
                type: "varchar(32)",
                unicode: false,
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(20)",
                oldUnicode: false,
                oldMaxLength: 20);

            migrationBuilder.AddColumn<int>(
                name: "MarketType",
                table: "Stocks",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_MarketType_Symbol",
                table: "Stocks",
                columns: new[] { "MarketType", "Symbol" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Stocks_MarketType_Symbol",
                table: "Stocks");

            migrationBuilder.DropColumn(
                name: "MarketType",
                table: "Stocks");

            migrationBuilder.AlterColumn<string>(
                name: "YahooSymbol",
                table: "Stocks",
                type: "varchar(25)",
                unicode: false,
                maxLength: 25,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(32)",
                oldUnicode: false,
                oldMaxLength: 32,
                oldDefaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "Symbol",
                table: "Stocks",
                type: "varchar(20)",
                unicode: false,
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(32)",
                oldUnicode: false,
                oldMaxLength: 32);

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_Symbol",
                table: "Stocks",
                column: "Symbol",
                unique: true);
        }
    }
}
