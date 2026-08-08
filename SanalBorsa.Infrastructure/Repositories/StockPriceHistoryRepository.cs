using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces.Repositories;
using SanalBorsa.Domain.Models;
using SanalBorsa.Infrastructure.Data;

namespace SanalBorsa.Infrastructure.Repositories;

public class StockPriceHistoryRepository : BaseRepository<StockPriceHistory>, IStockPriceHistoryRepository
{
    public StockPriceHistoryRepository(AppDbContext context) : base(context) { }

    public async Task<IReadOnlyList<StockPriceHistory>> GetByStockIdAsync(
        int stockId,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default)
    {
        var query = DbSet.Where(p => p.StockId == stockId);

        if (from.HasValue)
            query = query.Where(p => p.Date >= from.Value.Date);

        if (to.HasValue)
            query = query.Where(p => p.Date <= to.Value.Date);

        return await query
            .OrderBy(p => p.Date)
            .ToListAsync(ct);
    }

    public async Task<StockPriceHistory?> GetLatestByStockIdAsync(int stockId, CancellationToken ct = default)
        => await DbSet
            .Where(p => p.StockId == stockId)
            .OrderByDescending(p => p.Date)
            .FirstOrDefaultAsync(ct);

    public async Task<StockPriceHistory?> GetEarliestByStockIdAsync(int stockId, CancellationToken ct = default)
        => await DbSet
            .Where(p => p.StockId == stockId)
            .OrderBy(p => p.Date)
            .FirstOrDefaultAsync(ct);

    public async Task DeleteAllByStockIdAsync(int stockId, CancellationToken ct = default)
        => await DbSet
            .Where(p => p.StockId == stockId)
            .ExecuteDeleteAsync(ct);

    public async Task DeleteByStockIdAndDateRangeAsync(
        int stockId,
        DateTime fromInclusive,
        DateTime toInclusive,
        CancellationToken ct = default)
        => await DbSet
            .Where(p =>
                p.StockId == stockId
                && p.Date >= fromInclusive.Date
                && p.Date <= toInclusive.Date)
            .ExecuteDeleteAsync(ct);

    public async Task<int> DeleteAllAsync(CancellationToken ct = default)
    {
        Context.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));

        // Hisse bazlı silme — indeksli, timeout’a daha dayanıklı
        var stockIds = await Context.Stocks.AsNoTracking().Select(s => s.Id).ToListAsync(ct);
        var total = 0;
        foreach (var id in stockIds)
        {
            total += await DbSet.Where(p => p.StockId == id).ExecuteDeleteAsync(ct);
        }

        return total;
    }

    public async Task<int> DeleteCreatedBeforeAsync(DateTime createdBeforeUtc, CancellationToken ct = default)
    {
        Context.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
        return await DbSet
            .Where(p => p.CreatedAt < createdBeforeUtc)
            .ExecuteDeleteAsync(ct);
    }

    public async Task<bool> AnyAsync(CancellationToken ct = default)
        => await DbSet.AnyAsync(ct);

    /// <summary>
    /// SqlBulkCopy ile tek round-trip'lik akış — chunked AddRange + SaveChanges (2000'lik
    /// parçalarda ayrı ayrı gidiş-dönüş) 10.000 barlık bir hissede tek başına onlarca saniye
    /// sürüyordu (bkz. proje sohbeti — BAC/BALL ölçümü: TV verisi gelip DB yazımı bitene kadar
    /// 44-60 saniye). TimeMachineLeaderRepository'deki aynı desen.
    /// </summary>
    public async Task BulkInsertAsync(IEnumerable<StockPriceHistory> records, CancellationToken ct = default)
    {
        var list = records
            .GroupBy(r => new { r.StockId, Date = r.Date.Date })
            .Select(g => g.Last())
            .ToList();

        if (list.Count == 0)
            return;

        var table = BuildPriceHistoryTable(list);
        var connection = (Microsoft.Data.SqlClient.SqlConnection)Context.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        if (openedHere)
            await connection.OpenAsync(ct);

        try
        {
            using var bulk = new Microsoft.Data.SqlClient.SqlBulkCopy(
                connection,
                Microsoft.Data.SqlClient.SqlBulkCopyOptions.Default,
                (Microsoft.Data.SqlClient.SqlTransaction?)Context.Database.CurrentTransaction?.GetDbTransaction())
            {
                DestinationTableName = "StockPriceHistories",
                BatchSize = 10_000,
                BulkCopyTimeout = 600,
            };

            foreach (System.Data.DataColumn column in table.Columns)
                bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);

            await bulk.WriteToServerAsync(table, ct);
        }
        finally
        {
            if (openedHere)
                await connection.CloseAsync();
        }
    }

    private static System.Data.DataTable BuildPriceHistoryTable(IReadOnlyList<StockPriceHistory> rows)
    {
        var table = new System.Data.DataTable("StockPriceHistories");
        table.Columns.Add(nameof(StockPriceHistory.StockId), typeof(int));
        table.Columns.Add(nameof(StockPriceHistory.Date), typeof(DateTime));
        table.Columns.Add(nameof(StockPriceHistory.Open), typeof(decimal));
        table.Columns.Add(nameof(StockPriceHistory.High), typeof(decimal));
        table.Columns.Add(nameof(StockPriceHistory.Low), typeof(decimal));
        table.Columns.Add(nameof(StockPriceHistory.Close), typeof(decimal));
        table.Columns.Add(nameof(StockPriceHistory.AdjustedClose), typeof(decimal));
        table.Columns.Add(nameof(StockPriceHistory.Volume), typeof(long));
        table.Columns.Add(nameof(StockPriceHistory.CreatedAt), typeof(DateTime));

        foreach (var row in rows)
        {
            table.Rows.Add(
                row.StockId,
                row.Date.Date,
                row.Open,
                row.High,
                row.Low,
                row.Close,
                row.AdjustedClose,
                row.Volume,
                row.CreatedAt);
        }

        return table;
    }

    public async Task<IReadOnlyDictionary<int, MarketPriceSnapshot>> GetMarketSnapshotsAsync(
        IReadOnlyList<int> stockIds,
        int sparklineDays = 28,
        CancellationToken ct = default,
        int? windowDays = null)
    {
        if (stockIds.Count == 0)
            return new Dictionary<int, MarketPriceSnapshot>();

        // windowDays: kaç gün geriye bakılacak. Belirtilmezse sparklineDays + 5 gün.
        var effectiveWindow = windowDays ?? (sparklineDays + 5);
        var from = DateTime.UtcNow.Date.AddDays(-effectiveWindow);
        var records = await DbSet
            .AsNoTracking()
            .Where(p => stockIds.Contains(p.StockId) && p.Date >= from)
            .OrderBy(p => p.StockId)
            .ThenBy(p => p.Date)
            .ToListAsync(ct);

        var result = records
            .GroupBy(p => p.StockId)
            .ToDictionary(g => g.Key, g => BuildSnapshot(g.ToList(), sparklineDays));

        // Pencere içinde kaydı olmayan stock'lar için en son kaydı fallback olarak al
        var missingIds = stockIds.Where(id => !result.ContainsKey(id)).ToList();
        if (missingIds.Count > 0)
        {
            // Her eksik stock için son 28 kaydı çek; çok veri yüklemeden sparkline + son fiyat oluşturulur
            foreach (var missingId in missingIds)
            {
                var fallback = await DbSet
                    .AsNoTracking()
                    .Where(p => p.StockId == missingId)
                    .OrderByDescending(p => p.Date)
                    .Take(sparklineDays + 2)
                    .OrderBy(p => p.Date)
                    .ToListAsync(ct);

                if (fallback.Count > 0)
                    result[missingId] = BuildSnapshot(fallback, sparklineDays);
            }
        }

        return result;
    }

    private static MarketPriceSnapshot BuildSnapshot(IReadOnlyList<StockPriceHistory> ordered, int sparklineDays)
    {
        if (ordered.Count == 0)
            return new MarketPriceSnapshot(null, null, null, null, []);

        var latest = ordered[^1];
        var previous = ordered.Count > 1 ? ordered[^2] : null;
        var sparkline = ordered
            .TakeLast(sparklineDays)
            .Select(p => p.Close)
            .ToList();

        return new MarketPriceSnapshot(
            latest.Close,
            latest.Open,
            previous?.Close,
            latest.Volume,
            sparkline);
    }

    public async Task<DateTime?> GetLatestTradingDateAsync(CancellationToken ct = default)
    {
        if (!await DbSet.AnyAsync(ct)) return null;
        return await DbSet.MaxAsync(p => p.Date, ct);
    }

    public async Task<DateTime?> GetLatestTradingDateForMarketAsync(
        MarketType marketType,
        CancellationToken ct = default)
    {
        var q =
            from p in DbSet.AsNoTracking()
            join s in Context.Set<Stock>().AsNoTracking() on p.StockId equals s.Id
            where s.MarketType == marketType && s.IsActive
            select (DateTime?)p.Date;

        return await q.MaxAsync(ct);
    }

    public async Task<IReadOnlyDictionary<int, (DateTime Date, decimal Close, decimal AdjustedClose)>> GetClosesOnOrBeforeAsync(
        IReadOnlyList<int> stockIds,
        DateTime onOrBefore,
        CancellationToken ct = default)
    {
        if (stockIds.Count == 0)
            return new Dictionary<int, (DateTime, decimal, decimal)>();

        var cutoff = onOrBefore.Date;
        var ids = stockIds.Distinct().ToList();

        // Son işlem günü (cutoff dahil) her hisse için
        var dateRows = await DbSet
            .AsNoTracking()
            .Where(p => ids.Contains(p.StockId) && p.Date <= cutoff)
            .GroupBy(p => p.StockId)
            .Select(g => new { StockId = g.Key, Date = g.Max(x => x.Date) })
            .ToListAsync(ct);

        if (dateRows.Count == 0)
            return new Dictionary<int, (DateTime, decimal, decimal)>();

        var keys = dateRows.Select(r => (r.StockId, r.Date)).ToHashSet();
        var stockIdSet = dateRows.Select(r => r.StockId).ToHashSet();
        var minDate = dateRows.Min(r => r.Date);

        var prices = await DbSet
            .AsNoTracking()
            .Where(p => stockIdSet.Contains(p.StockId) && p.Date >= minDate && p.Date <= cutoff)
            .Select(p => new { p.StockId, p.Date, p.Close, p.AdjustedClose })
            .ToListAsync(ct);

        var result = new Dictionary<int, (DateTime, decimal, decimal)>();
        foreach (var p in prices)
        {
            if (!keys.Contains((p.StockId, p.Date))) continue;
            result[p.StockId] = (p.Date, p.Close, p.AdjustedClose);
        }

        return result;
    }

    public async Task<IReadOnlyList<DailyClose>> GetDailyClosesAsync(
        MarketType marketType,
        DateTime fromInclusive,
        DateTime toInclusive,
        CancellationToken ct = default)
    {
        var rangeStart = fromInclusive.Date;
        var rangeEnd = toInclusive.Date;

        // BIST tüm hisse × yıllarca günlük bar — default 30 sn yetmez
        var previousTimeout = Context.Database.GetCommandTimeout();
        Context.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
        try
        {
            var query =
                from p in DbSet.AsNoTracking()
                join s in Context.Set<Stock>().AsNoTracking() on p.StockId equals s.Id
                where s.MarketType == marketType
                      && s.IsActive
                      && p.Date >= rangeStart
                      && p.Date <= rangeEnd
                orderby p.Date
                select new DailyClose(p.StockId, p.Date, p.Close, p.AdjustedClose);

            return await query.ToListAsync(ct);
        }
        finally
        {
            Context.Database.SetCommandTimeout(previousTimeout);
        }
    }

    public async Task<IReadOnlyList<DailyClose>> GetDailyClosesForStockIdsAsync(
        IReadOnlyList<int> stockIds,
        DateTime fromInclusive,
        DateTime toInclusive,
        CancellationToken ct = default)
    {
        if (stockIds.Count == 0)
            return [];

        var rangeStart = fromInclusive.Date;
        var rangeEnd = toInclusive.Date;
        const int batchSize = 150;
        var all = new List<DailyClose>();

        for (var i = 0; i < stockIds.Count; i += batchSize)
        {
            var batch = stockIds.Skip(i).Take(batchSize).ToList();
            var rows = await DbSet.AsNoTracking()
                .Where(p => batch.Contains(p.StockId)
                            && p.Date >= rangeStart
                            && p.Date <= rangeEnd)
                .OrderBy(p => p.StockId)
                .ThenBy(p => p.Date)
                .Select(p => new DailyClose(p.StockId, p.Date, p.Close, p.AdjustedClose))
                .ToListAsync(ct);
            all.AddRange(rows);
        }

        return all;
    }

    /// <summary>
    /// Eski hâli tüm satırları (izlenen entity olarak) çekip tek tek karşılaştırıp tek büyük
    /// SaveChanges ile güncelliyordu — 10.000 satırlık bir hissede bu 30+ saniye sürüyordu (bkz.
    /// proje sohbeti). Şimdi: gelen (tarih, değer) çiftlerini SqlBulkCopy ile bir #temp tabloya
    /// yazıp tek bir UPDATE...FROM...JOIN ile hepsini bir seferde güncelliyoruz — EF change
    /// tracking'e hiç girmeden, set-bazlı tek SQL ifadesi.
    /// </summary>
    public async Task<int> UpdateAdjustedClosesAsync(
        int stockId,
        IReadOnlyDictionary<DateTime, decimal> adjustedByDate,
        CancellationToken ct = default)
    {
        if (adjustedByDate.Count == 0)
            return 0;

        var connection = (Microsoft.Data.SqlClient.SqlConnection)Context.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        if (openedHere)
            await connection.OpenAsync(ct);

        var tempTable = $"#AdjClose_{Guid.NewGuid():N}";
        try
        {
            var table = new System.Data.DataTable();
            table.Columns.Add("Date", typeof(DateTime));
            table.Columns.Add("AdjustedClose", typeof(decimal));
            foreach (var (date, value) in adjustedByDate)
                table.Rows.Add(date.Date, Math.Round(value, 10));

            var tx = (Microsoft.Data.SqlClient.SqlTransaction?)Context.Database.CurrentTransaction?.GetDbTransaction();

            using (var create = connection.CreateCommand())
            {
                create.Transaction = tx;
                create.CommandText = $"CREATE TABLE {tempTable} (Date date NOT NULL PRIMARY KEY, AdjustedClose decimal(24,10) NOT NULL);";
                await create.ExecuteNonQueryAsync(ct);
            }

            using (var bulk = new Microsoft.Data.SqlClient.SqlBulkCopy(connection, Microsoft.Data.SqlClient.SqlBulkCopyOptions.Default, tx)
            {
                DestinationTableName = tempTable,
                BulkCopyTimeout = 600,
            })
            {
                bulk.ColumnMappings.Add("Date", "Date");
                bulk.ColumnMappings.Add("AdjustedClose", "AdjustedClose");
                await bulk.WriteToServerAsync(table, ct);
            }

            using var update = connection.CreateCommand();
            update.Transaction = tx;
            update.CommandTimeout = 300;
            update.CommandText =
                $"UPDATE p SET p.AdjustedClose = s.AdjustedClose " +
                $"FROM StockPriceHistories p JOIN {tempTable} s ON p.Date = s.Date " +
                $"WHERE p.StockId = @stockId AND p.AdjustedClose <> s.AdjustedClose;";
            var stockIdParam = update.CreateParameter();
            stockIdParam.ParameterName = "@stockId";
            stockIdParam.Value = stockId;
            update.Parameters.Add(stockIdParam);
            var updated = await update.ExecuteNonQueryAsync(ct);

            return updated;
        }
        finally
        {
            try
            {
                using var drop = connection.CreateCommand();
                drop.Transaction = (Microsoft.Data.SqlClient.SqlTransaction?)Context.Database.CurrentTransaction?.GetDbTransaction();
                drop.CommandText = $"IF OBJECT_ID('tempdb..{tempTable}') IS NOT NULL DROP TABLE {tempTable};";
                await drop.ExecuteNonQueryAsync(CancellationToken.None);
            }
            catch { /* best-effort cleanup */ }

            if (openedHere)
                await connection.CloseAsync();
        }
    }

}
