using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SanalBorsa.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IncreaseTimeMachineLeaderReturnPctPrecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "ReturnPct",
                table: "TimeMachineLeaders",
                type: "decimal(24,10)",
                precision: 24,
                scale: 10,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "ReturnPct",
                table: "TimeMachineLeaders",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(24,10)",
                oldPrecision: 24,
                oldScale: 10);
        }
    }
}
