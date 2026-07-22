using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SanalBorsa.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCryptoPortfolioSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PortfolioHoldings_PortfolioId_Symbol",
                table: "PortfolioHoldings");

            migrationBuilder.AddColumn<decimal>(
                name: "CashUsd",
                table: "UserPortfolios",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 100000m);

            migrationBuilder.AlterColumn<decimal>(
                name: "Total",
                table: "PortfolioTransactions",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<string>(
                name: "Symbol",
                table: "PortfolioTransactions",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "PortfolioTransactions",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AddColumn<string>(
                name: "FillBreakdownJson",
                table: "PortfolioTransactions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MarketType",
                table: "PortfolioTransactions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<decimal>(
                name: "Quantity",
                table: "PortfolioTransactions",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("UPDATE PortfolioTransactions SET Quantity = CAST(Lots AS decimal(18,8))");

            migrationBuilder.DropColumn(
                name: "Lots",
                table: "PortfolioTransactions");

            migrationBuilder.AlterColumn<string>(
                name: "Symbol",
                table: "PortfolioHoldings",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<decimal>(
                name: "AvgCost",
                table: "PortfolioHoldings",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AddColumn<int>(
                name: "MarketType",
                table: "PortfolioHoldings",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<decimal>(
                name: "Quantity",
                table: "PortfolioHoldings",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("UPDATE PortfolioHoldings SET Quantity = CAST(Lots AS decimal(18,8))");

            migrationBuilder.DropColumn(
                name: "Lots",
                table: "PortfolioHoldings");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioHoldings_PortfolioId_Symbol_MarketType",
                table: "PortfolioHoldings",
                columns: new[] { "PortfolioId", "Symbol", "MarketType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PortfolioHoldings_PortfolioId_Symbol_MarketType",
                table: "PortfolioHoldings");

            migrationBuilder.DropColumn(
                name: "CashUsd",
                table: "UserPortfolios");

            migrationBuilder.DropColumn(
                name: "FillBreakdownJson",
                table: "PortfolioTransactions");

            migrationBuilder.DropColumn(
                name: "MarketType",
                table: "PortfolioTransactions");

            migrationBuilder.AddColumn<long>(
                name: "Lots",
                table: "PortfolioTransactions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.Sql("UPDATE PortfolioTransactions SET Lots = CAST(Quantity AS bigint)");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "PortfolioTransactions");

            migrationBuilder.DropColumn(
                name: "MarketType",
                table: "PortfolioHoldings");

            migrationBuilder.AddColumn<long>(
                name: "Lots",
                table: "PortfolioHoldings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.Sql("UPDATE PortfolioHoldings SET Lots = CAST(Quantity AS bigint)");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "PortfolioHoldings");

            migrationBuilder.AlterColumn<decimal>(
                name: "Total",
                table: "PortfolioTransactions",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,8)",
                oldPrecision: 18,
                oldScale: 8);

            migrationBuilder.AlterColumn<string>(
                name: "Symbol",
                table: "PortfolioTransactions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "PortfolioTransactions",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,8)",
                oldPrecision: 18,
                oldScale: 8);

            migrationBuilder.AlterColumn<string>(
                name: "Symbol",
                table: "PortfolioHoldings",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<decimal>(
                name: "AvgCost",
                table: "PortfolioHoldings",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,8)",
                oldPrecision: 18,
                oldScale: 8);

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioHoldings_PortfolioId_Symbol",
                table: "PortfolioHoldings",
                columns: new[] { "PortfolioId", "Symbol" },
                unique: true);
        }
    }
}
