using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Enums;
using SanalBorsa.Domain.Interfaces.Repositories;
using SanalBorsa.Infrastructure.Data;

namespace SanalBorsa.Infrastructure.Repositories;

public class TimeMachineLeaderRepository : ITimeMachineLeaderRepository
{
    private const string TableName = "TimeMachineLeaders";

    private readonly AppDbContext _context;
    private readonly DbSet<TimeMachineLeader> _set;

    public TimeMachineLeaderRepository(AppDbContext context)
    {
        _context = context;
        _set = context.Set<TimeMachineLeader>();
    }

    public async Task<IReadOnlyList<TimeMachineLeader>> GetForDateAsync(
        TimeMachineCategory category,
        DateTime onOrBefore,
        CancellationToken ct = default)
    {
        var cutoff = onOrBefore.Date;

        var effectiveDate = await _set
            .AsNoTracking()
            .Where(l => l.Category == category && l.StartDate <= cutoff)
            .MaxAsync(l => (DateTime?)l.StartDate, ct);

        if (effectiveDate is null)
            return [];

        return await _set
            .AsNoTracking()
            .Where(l => l.Category == category && l.StartDate == effectiveDate.Value)
            .OrderBy(l => l.Rank)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TimeMachineLeaderStats>> GetStatsAsync(CancellationToken ct = default)
        => await _set
            .AsNoTracking()
            .GroupBy(l => l.Category)
            .Select(g => new TimeMachineLeaderStats(
                g.Key,
                g.Count(),
                g.Min(x => (DateTime?)x.StartDate),
                g.Max(x => (DateTime?)x.StartDate),
                g.Max(x => (DateTime?)x.EndDate),
                g.Max(x => (DateTime?)x.ComputedAt)))
            .ToListAsync(ct);

    /// <summary>
    /// Kategori komple yenilenir. Satır sayısı on binlerle ifade edildiği için
    /// EF change tracker yerine SqlBulkCopy kullanılır (tek round-trip'lik akış).
    /// </summary>
    public async Task ReplaceCategoryAsync(
        TimeMachineCategory category,
        IReadOnlyList<TimeMachineLeader> rows,
        CancellationToken ct = default)
    {
        var previousTimeout = _context.Database.GetCommandTimeout();
        _context.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));

        try
        {
            await _set.Where(l => l.Category == category).ExecuteDeleteAsync(ct);

            if (rows.Count == 0)
                return;

            var table = BuildTable(rows);
            var connection = (SqlConnection)_context.Database.GetDbConnection();
            var openedHere = connection.State != ConnectionState.Open;
            if (openedHere)
                await connection.OpenAsync(ct);

            try
            {
                using var bulk = new SqlBulkCopy(
                    connection,
                    SqlBulkCopyOptions.Default,
                    (SqlTransaction?)_context.Database.CurrentTransaction?.GetDbTransaction())
                {
                    DestinationTableName = TableName,
                    BatchSize = 10_000,
                    BulkCopyTimeout = 600,
                };

                foreach (DataColumn column in table.Columns)
                    bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);

                await bulk.WriteToServerAsync(table, ct);
            }
            finally
            {
                if (openedHere)
                    await connection.CloseAsync();
            }
        }
        finally
        {
            _context.Database.SetCommandTimeout(previousTimeout);
        }
    }

    /// <summary>Kolon şeması — Id IDENTITY olduğu için yazılmaz.</summary>
    private static DataTable BuildTable(IReadOnlyList<TimeMachineLeader> rows)
    {
        var table = new DataTable(TableName);
        table.Columns.Add(nameof(TimeMachineLeader.Category), typeof(int));
        table.Columns.Add(nameof(TimeMachineLeader.StartDate), typeof(DateTime));
        table.Columns.Add(nameof(TimeMachineLeader.Rank), typeof(int));
        table.Columns.Add(nameof(TimeMachineLeader.StockId), typeof(int));
        table.Columns.Add(nameof(TimeMachineLeader.Symbol), typeof(string));
        table.Columns.Add(nameof(TimeMachineLeader.Name), typeof(string));
        table.Columns.Add(nameof(TimeMachineLeader.StartPrice), typeof(decimal));
        table.Columns.Add(nameof(TimeMachineLeader.EndPrice), typeof(decimal));
        table.Columns.Add(nameof(TimeMachineLeader.ReturnPct), typeof(decimal));
        table.Columns.Add(nameof(TimeMachineLeader.EndDate), typeof(DateTime));
        table.Columns.Add(nameof(TimeMachineLeader.ComputedAt), typeof(DateTime));

        foreach (var row in rows)
        {
            table.Rows.Add(
                (int)row.Category,
                row.StartDate.Date,
                row.Rank,
                row.StockId,
                row.Symbol,
                row.Name,
                row.StartPrice,
                row.EndPrice,
                row.ReturnPct,
                row.EndDate.Date,
                row.ComputedAt);
        }

        return table;
    }
}
