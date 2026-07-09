namespace SanalBorsa.Domain.Interfaces.Repositories;

public interface IRepository<TEntity> where TEntity : class
{
    Task<TEntity?> GetByIdAsync(object id, CancellationToken ct = default);

    Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken ct = default);

    Task AddAsync(TEntity entity, CancellationToken ct = default);

    Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default);

    void Update(TEntity entity);

    void Remove(TEntity entity);

    void RemoveRange(IEnumerable<TEntity> entities);
}
