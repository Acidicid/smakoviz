using Menu.Domain.Common;
using Menu.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Menu.Dal.Context;

/// <summary>
/// DbContext контексту «Меню». Code-First: схема БД генерується з моделей міграціями
/// (папка Migrations/). Уся конфігурація — в окремих класах Configurations/*Configuration.cs.
/// </summary>
public class MenuDbContext(DbContextOptions<MenuDbContext> options) : DbContext(options)
{
    /// <summary>Хто змінює дані — пишеться в аудитні колонки (у Лаб. 06 — з JWT).</summary>
    public const string SystemUser = "menu-api";

    public DbSet<Restaurant> Restaurants => Set<Restaurant>();
    public DbSet<RestaurantProfile> RestaurantProfiles => Set<RestaurantProfile>();
    public DbSet<Dish> Dishes => Set<Dish>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<DishCategory> DishCategories => Set<DishCategory>();

    /// <summary>
    /// Усі IEntityTypeConfiguration&lt;T&gt; з цієї збірки підключаються ОДНИМ рядком —
    /// однаковий підхід до всіх сутностей, а OnModelCreating не розростається.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(MenuDbContext).Assembly);

    /// <summary>
    /// Перед збереженням: аудит (CreatedAt/By, UpdatedAt/By) і м'яке видалення —
    /// Remove() для AuditableEntity перетворюється на UPDATE ... SET IsDeleted = 1.
    /// Так репозиторії лише змінюють стан, а правила збереження живуть в одному місці.
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Property(e => e.CreatedAt).CurrentValue = now;
                    entry.Property(e => e.CreatedBy).CurrentValue = SystemUser;
                    break;

                case EntityState.Modified:
                    entry.Property(e => e.UpdatedAt).CurrentValue = now;
                    entry.Property(e => e.UpdatedBy).CurrentValue = SystemUser;
                    break;

                case EntityState.Deleted:
                    entry.State = EntityState.Modified;
                    entry.Property(e => e.IsDeleted).CurrentValue = true;
                    entry.Property(e => e.UpdatedAt).CurrentValue = now;
                    entry.Property(e => e.UpdatedBy).CurrentValue = SystemUser;
                    break;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
