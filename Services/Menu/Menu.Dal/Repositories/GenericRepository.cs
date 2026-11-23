using Ardalis.Specification;
using Ardalis.Specification.EntityFrameworkCore;
using Menu.Dal.Context;
using Microsoft.EntityFrameworkCore;

namespace Menu.Dal.Repositories;

public class GenericRepository<T> : IGenericRepository<T> where T : class
{
    protected readonly MenuDbContext Context;
    protected readonly DbSet<T> Set;

    public GenericRepository(MenuDbContext context)
    {
        Context = context;
        Set = context.Set<T>();
    }

    public async Task<T?> GetByIdAsync(object id, CancellationToken ct = default)
        => await Set.FindAsync([id], ct);

    public async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default)
        => await Set.AsNoTracking().ToListAsync(ct);

    public async Task AddAsync(T entity, CancellationToken ct = default)
        => await Set.AddAsync(entity, ct);

    /// <summary>Лише позначає сутність зміненою — в БД нічого не йде до UnitOfWork.SaveChangesAsync.</summary>
    public void Update(T entity) => Set.Update(entity);

    /// <summary>Лише позначає для видалення (для AuditableEntity контекст перетворить це на м'яке видалення).</summary>
    public void Delete(T entity) => Set.Remove(entity);

    // ---------- Specification pattern ----------
    // Репозиторій не знає, ЯКІ саме критерії потрібні: він лише застосовує специфікацію
    // (Where / Include / OrderBy / Skip / Take) до DbSet і виконує ОДИН SQL-запит.
    // Нова специфікація з іншими критеріями працює без жодних змін тут.

    public async Task<IReadOnlyList<T>> ListAsync(ISpecification<T> specification, CancellationToken ct = default)
        => await Set.WithSpecification(specification).ToListAsync(ct);

    /// <summary>
    /// Кількість записів, що відповідають специфікації (SELECT COUNT(*) рахує БД).
    /// Для totalCount сервіс передає ту саму специфікацію БЕЗ пагінації.
    /// </summary>
    public Task<int> CountAsync(ISpecification<T> specification, CancellationToken ct = default)
        => Set.WithSpecification(specification).CountAsync(ct);

    public Task<T?> FirstOrDefaultAsync(ISpecification<T> specification, CancellationToken ct = default)
        => Set.WithSpecification(specification).FirstOrDefaultAsync(ct);
}
