using Menu.Dal.Context;
using Menu.Domain.Exceptions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Menu.Dal.Repositories;

/// <summary>
/// Unit of Work над одним MenuDbContext (Scoped — один на HTTP-запит).
/// Репозиторії лише додають/змінюють/видаляють сутності в Change Tracker,
/// а ЄДИНИЙ SaveChangesAsync відправляє всі зміни в БД однією транзакцією
/// (EF Core сам обгортає SaveChanges у транзакцію).
/// В eShop цю роль виконує сам DbContext (OrderingContext : IUnitOfWork).
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly MenuDbContext _context;

    public UnitOfWork(MenuDbContext context)
    {
        _context = context;
        Restaurants = new RestaurantRepository(context);
        Dishes = new DishRepository(context);
        Categories = new CategoryRepository(context);
        DishCategories = new DishCategoryRepository(context);
    }

    public IRestaurantRepository Restaurants { get; }
    public IDishRepository Dishes { get; }
    public ICategoryRepository Categories { get; }
    public IDishCategoryRepository DishCategories { get; }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // RowVersion не збігся: хтось змінив рядок після того, як ми його прочитали
            throw new ConflictException("The record was changed by someone else. Reload it and try again");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // друга лінія захисту унікальності (на випадок паралельних запитів після перевірки в сервісі)
            throw new ConflictException("A record with the same unique value already exists");
        }
    }
}
