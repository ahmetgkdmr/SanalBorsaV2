using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SanalBorsa.Infrastructure.Data;

#nullable disable

namespace SanalBorsa.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260726180000_AddTimeMachineLeaders")]
public class AddTimeMachineLeaders : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'dbo.TimeMachineLeaders', N'U') IS NULL
            BEGIN
                CREATE TABLE [TimeMachineLeaders] (
                    [Id]         bigint IDENTITY(1,1) NOT NULL,
                    [Category]   int            NOT NULL,
                    [StartDate]  date           NOT NULL,
                    [Rank]       int            NOT NULL,
                    [StockId]    int            NOT NULL,
                    [Symbol]     nvarchar(32)   NOT NULL,
                    [Name]       nvarchar(200)  NOT NULL,
                    [StartPrice] decimal(18, 6) NOT NULL,
                    [EndPrice]   decimal(18, 6) NOT NULL,
                    [ReturnPct]  decimal(18, 4) NOT NULL,
                    [EndDate]    date           NOT NULL,
                    [ComputedAt] datetime2      NOT NULL,
                    CONSTRAINT [PK_TimeMachineLeaders] PRIMARY KEY ([Id])
                );

                CREATE UNIQUE INDEX [IX_TimeMachineLeaders_Category_StartDate_Rank]
                    ON [TimeMachineLeaders] ([Category], [StartDate], [Rank]);
            END
            """);

        // Gece işi fiyat tablosunu tarihe göre sıralı tarar; bu indeks olmadan
        // her tarama 3M+ satırlık sıralama demek.
        migrationBuilder.Sql("""
            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = N'IX_StockPriceHistories_Date_StockId'
                  AND object_id = OBJECT_ID(N'dbo.StockPriceHistories'))
            BEGIN
                CREATE NONCLUSTERED INDEX [IX_StockPriceHistories_Date_StockId]
                    ON [StockPriceHistories] ([Date], [StockId]) INCLUDE ([Close]);
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS [IX_StockPriceHistories_Date_StockId] ON [StockPriceHistories];
            DROP TABLE IF EXISTS [TimeMachineLeaders];
            """);
    }
}
