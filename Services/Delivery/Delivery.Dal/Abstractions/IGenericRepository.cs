namespace Delivery.Dal.Abstractions;

/// <summary>
/// Узагальнений CRUD — однакові операції для всіх сутностей пишуться ОДИН раз
/// (реалізація — BaseDapperRepository&lt;T&gt;). Конкретні інтерфейси успадковують його
/// й додають лише вузькоспеціалізовані запити (JOIN, агрегації, процедури).
/// Методи повертають моделі, а не SqlDataReader/DataTable: BLL не знає, що під капотом SQL.
/// </summary>
public interface IGenericRepository<T> where T : class
{
    Task<T?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default);
    Task<long> AddAsync(T entity, CancellationToken ct = default);
    Task<bool> DeleteAsync(long id, CancellationToken ct = default);
}
