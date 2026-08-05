using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SanalBorsa.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStockIntradayBars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockIntradayBars",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StockId = table.Column<int>(type: "int", nullable: false),
                    BarTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Close = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockIntradayBars", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockIntradayBars_Stocks_StockId",
                        column: x => x.StockId,
                        principalTable: "Stocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockIntradayBars_StockId",
                table: "StockIntradayBars",
                column: "StockId")
                .Annotation("SqlServer:Include", new[] { "BarTime", "Close" });

            migrationBuilder.CreateIndex(
                name: "IX_StockIntradayBars_StockId_BarTime",
                table: "StockIntradayBars",
                columns: new[] { "StockId", "BarTime" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockIntradayBars");
        }
    }
}
