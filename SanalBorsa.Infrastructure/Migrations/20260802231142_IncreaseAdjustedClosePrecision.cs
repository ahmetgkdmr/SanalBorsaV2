using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SanalBorsa.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IncreaseAdjustedClosePrecision : Migration
    {
        /// <summary>
        /// ALTER COLUMN [AdjustedClose] decimal(24,10) SQL Server'da tüm tabloyu fiziksel olarak
        /// yeniden yazmayı gerektiriyor (storage boyutu değişiyor) — milyonlarca satırlı
        /// StockPriceHistories'te bu, bağlantı ağ tarafından "boşta" sayılıp kesilene kadar
        /// dakikalarca sürüyordu (birkaç kez zaman aşımına uğradı). Bunun yerine metadata-only
        /// (satırları taramayan, saniyeler süren) bir desen: yeni kolonu sabit varsayılanla ekle,
        /// eskisini sil, yeniden adlandır. Eski (zaten 4 ondalığa yuvarlanmış, hatalı) değerleri
        /// taşımaya gerek yok — bu migration'dan sonra AdjustedClose sync'i (BIST + ABD, TradingView)
        /// baştan çalıştırılıp doğru hassasiyetle yeniden yazılacak.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE [StockPriceHistories] ADD [AdjustedClose_New] decimal(24,10) NOT NULL DEFAULT 0;");
            migrationBuilder.Sql(
                "ALTER TABLE [StockPriceHistories] DROP COLUMN [AdjustedClose];");
            migrationBuilder.Sql(
                "EXEC sp_rename 'StockPriceHistories.AdjustedClose_New', 'AdjustedClose', 'COLUMN';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE [StockPriceHistories] ADD [AdjustedClose_Old] decimal(18,4) NOT NULL DEFAULT 0;");
            migrationBuilder.Sql(
                "ALTER TABLE [StockPriceHistories] DROP COLUMN [AdjustedClose];");
            migrationBuilder.Sql(
                "EXEC sp_rename 'StockPriceHistories.AdjustedClose_Old', 'AdjustedClose', 'COLUMN';");
        }
    }
}
