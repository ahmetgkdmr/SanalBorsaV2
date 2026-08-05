using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SanalBorsa.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UnifyPortfolioCashPool : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CashUsd",
                table: "UserPortfolios");

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRateAtTrade",
                table: "PortfolioTransactions",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExchangeRateAtTrade",
                table: "PortfolioTransactions");

            migrationBuilder.AddColumn<decimal>(
                name: "CashUsd",
                table: "UserPortfolios",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);
        }
    }
}
