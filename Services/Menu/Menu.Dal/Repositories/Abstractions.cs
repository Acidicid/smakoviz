using Ardalis.Specification;
using Menu.Domain.Entities;

namespace Menu.Dal.Repositories;

/// <summary>
/// Async Generic Repository — той самий контракт, що в Лаб. 02, але тепер його реалізує EF Core через DbSet&lt;T&gt;.
/// Методи, що звертаються до БД, — асинхронні; Update/Delete лише змінюють стан у контексті.
/// Репозиторій НЕ зберігає зміни сам: SaveChangesAsync є лише в Unit of Work.
/// </summary>
public interface IGenericRepository<T> where T : class
{
    Task<T?> GetByIdAsync(object id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(T entity, CancellationToken ct = default);
    void Update(T entity);
    void Delete(T entity);

    // ---- Specification pattern: репозиторій лише ЗАСТОСОВУЄ специфікацію (WithSpecification) ----
    Task<IReadOnlyList<T>> ListAsync(ISpecification<T> specification, CancellationToken ct = default);
    Task<int> CountAsync(ISpecification<T> specification, CancellationToken ct = default);
    Task<T?> FirstOrDefaultAsync(ISpecification<T> specification, CancellationToken ct = default);
}

/// <summary>Страви — EAGER LOADING: картка страви з усім пов'язаним ОДНИМ запитом.</summary>
public interface IDishRepository : IGenericRepository<Dish>
{
    Task<Dish?> GetWithDetailsAsync(long id, CancellationToken ct = default);
    Task<bool> ExistsByNameAsync(long restaurantId, string name, long? exceptDishId = null, CancellationToken ct = default);

    /// <summary>Очікуваний RowVersion від клієнта: якщо рядок змінився — SaveChanges дасть конфлікт (409).</summary>
    void SetExpectedRowVersion(Dish dish, byte[] rowVersion);
}

/// <summary>Ресторани — EXPLICIT LOADING: ресторан окремо, профіль і меню — окремими запитами, коли потрібні.</summary>
public interface IRestaurantRepository : IGenericRepository<Restaurant>
{
    Task<Restaurant?> GetWithMenuAsync(long id, bool onlyAvailable, CancellationToken ct = default);
    Task<Restaurant?> GetWithProfileAsync(long id, CancellationToken ct = default);
    Task<bool> ExistsBySlugAsync(string slug, long? exceptRestaurantId = null, CancellationToken ct = default);
    Task<bool> HasDishesAsync(long restaurantId, CancellationToken ct = default);
}

/// <summary>Категорії (довідник).</summary>
public interface ICategoryRepository : IGenericRepository<Category>
{
    Task<Category?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<bool> ExistsBySlugOrNameAsync(string slug, string name, CancellationToken ct = default);
}

/// <summary>Зв'язки страва ↔ категорія — LINQ to Entities для M:N через проміжну сутність і її власні поля.</summary>
public interface IDishCategoryRepository : IGenericRepository<DishCategory>
{
    /// <summary>Страви категорії, додані після since (поле AddedAt проміжної сутності).</summary>
    Task<IReadOnlyList<Dish>> GetRecentlyAddedAsync(string categorySlug, DateTime since, CancellationToken ct = default);

    /// <summary>Рекомендовані страви категорії (поле IsFeatured), спершу нові.</summary>
    Task<IReadOnlyList<Dish>> GetFeaturedAsync(int categoryId, int take, CancellationToken ct = default);

    /// <summary>Кількість доступних страв у кожній категорії (агрегація по проміжній таблиці).</summary>
    Task<IReadOnlyDictionary<int, int>> CountAvailableDishesByCategoryAsync(CancellationToken ct = default);
}

/// <summary>
/// Unit of Work: набір репозиторіїв над ОДНИМ DbContext і ЄДИНИЙ SaveChangesAsync.
/// Сервіс викликає його один раз наприкінці бізнес-операції — усі зміни йдуть у БД однією транзакцією.
/// </summary>
public interface IUnitOfWork
{
    IRestaurantRepository Restaurants { get; }
    IDishRepository Dishes { get; }
    ICategoryRepository Categories { get; }
    IDishCategoryRepository DishCategories { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
