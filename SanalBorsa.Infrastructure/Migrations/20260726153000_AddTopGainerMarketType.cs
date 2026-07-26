using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SanalBorsa.Infrastructure.Data;

#nullable disable

namespace SanalBorsa.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260726153000_AddTopGainerMarketType")]
public class AddTopGainerMarketType : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH(N'dbo.TopGainers', N'MarketType') IS NULL
            BEGIN
                DROP INDEX IF EXISTS [IX_TopGainers_Period_Rank] ON [TopGainers];

                ALTER TABLE [TopGainers] ALTER COLUMN [Symbol] nvarchar(32) NOT NULL;

                ALTER TABLE [TopGainers] ADD [MarketType] int NOT NULL
                    CONSTRAINT [DF_TopGainers_MarketType] DEFAULT (1);

                CREATE UNIQUE INDEX [IX_TopGainers_MarketType_Period_Rank]
                    ON [TopGainers] ([MarketType], [Period], [Rank]);
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH(N'dbo.TopGainers', N'MarketType') IS NOT NULL
            BEGIN
                DROP INDEX IF EXISTS [IX_TopGainers_MarketType_Period_Rank] ON [TopGainers];
                ALTER TABLE [TopGainers] DROP CONSTRAINT IF EXISTS [DF_TopGainers_MarketType];
                ALTER TABLE [TopGainers] DROP COLUMN [MarketType];
                ALTER TABLE [TopGainers] ALTER COLUMN [Symbol] nvarchar(20) NOT NULL;
                CREATE UNIQUE INDEX [IX_TopGainers_Period_Rank] ON [TopGainers] ([Period], [Rank]);
            END
            """);
    }
}
