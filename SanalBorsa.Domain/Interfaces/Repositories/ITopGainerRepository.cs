using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Enums;

namespace SanalBorsa.Domain.Interfaces.Repositories;

public interface ITopGainerRepository
{
    Task<IReadOnlyList<TopGainer>> GetAllAsync(CancellationToken ct = default);

    Task ReplaceAllAsync(IReadOnlyList<TopGainer> rows, CancellationToken ct = default);
}
