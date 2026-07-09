using Microsoft.EntityFrameworkCore;
using SanalBorsa.Domain.Interfaces.Repositories;
using SanalBorsa.Infrastructure.Data;

namespace SanalBorsa.Infrastructure.Repositories;

public abstract class BaseRepository<TEntity> : IRepository<TEntity> where TEntity : class
{
    protected readonly AppDbContext Context;
    protected readonly DbSet<TEntity> DbSet;

    protected BaseRepository(AppDbContext context)
    {
        Context = context;
        DbSet = context.Set<TEntity>();
    }

    public virtual async Task<TEntity?> GetByIdAsync(object id, CancellationToken ct = default)
        => await DbSet.FindAsync([id], ct);

    public virtual async Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken ct = default)
        => await DbSet.ToListAsync(ct);

    public virtual async Task AddAsync(TEntity entity, CancellationToken ct = default)
        => await DbSet.AddAsync(entity, ct);

    public virtual async Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default)
        => await DbSet.AddRangeAsync(entities, ct);

    public virtual void Update(TEntity entity)
        => DbSet.Update(entity);

    public virtual void Remove(TEntity entity)
        => DbSet.Remove(entity);

    public virtual void RemoveRange(IEnumerable<TEntity> entities)
        => DbSet.RemoveRange(entities);
}
