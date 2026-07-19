using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SanalBorsa.Infrastructure.Migrations;

[DbContext(typeof(Data.AppDbContext))]
[Migration("20260718210000_AddTopGainers")]
public class AddTopGainers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'[dbo].[TopGainers]', N'U') IS NULL
            BEGIN
                CREATE TABLE [TopGainers] (
                    [Id] int NOT NULL IDENTITY,
                    [Period] int NOT NULL,
                    [Rank] int NOT NULL,
                    [StockId] int NOT NULL,
                    [Symbol] nvarchar(20) NOT NULL,
                    [Name] nvarchar(200) NOT NULL,
                    [ReturnPct] decimal(18,4) NOT NULL,
                    [StartPrice] decimal(18,6) NOT NULL,
                    [EndPrice] decimal(18,6) NOT NULL,
                    [StartDate] date NOT NULL,
                    [EndDate] date NOT NULL,
                    [ComputedAt] datetime2 NOT NULL,
                    CONSTRAINT [PK_TopGainers] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_TopGainers_Stocks_StockId] FOREIGN KEY ([StockId]) REFERENCES [Stocks] ([Id]) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX [IX_TopGainers_Period_Rank] ON [TopGainers] ([Period], [Rank]);
                CREATE INDEX [IX_TopGainers_StockId] ON [TopGainers] ([StockId]);
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "TopGainers");
    }
}
