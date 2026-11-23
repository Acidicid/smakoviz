using Menu.Dal.Context;
using Menu.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Menu.Dal.Seeding;

/// <summary>
/// ПРОГРАМНИЙ сідінг — виконується в Program.cs ПІСЛЯ MigrateAsync() (довідник категорій
/// вже засіяно модельно через HasData у CategoryConfiguration).
///
/// Ідемпотентний: якщо ресторани вже є — нічого не робить, тож повторний запуск
/// застосунку дублів не створює. Вимикається налаштуванням Database:Seed=false
/// і ніколи не запускається в Production.
///
/// Порядок — за цілісністю FK: довідники (HasData) → ресторани з профілями → страви → зв'язки з категоріями.
/// Id ресторанів і страв — ті самі, що в db/p2/seed.sql і в копії страв сервісу «Доставка» (Лаб. 01–02).
/// </summary>
public static class DbInitializer
{
    public static async Task SeedAsync(MenuDbContext db, ILogger logger, CancellationToken ct = default)
    {
        if (await db.Restaurants.IgnoreQueryFilters().AnyAsync(ct))
        {
            logger.LogInformation("Seed skipped: database already contains {Entity}", "restaurants");
            return;
        }

        // Явні Id в IDENTITY-колонці потребують SET IDENTITY_INSERT ... ON у тій самій сесії,
        // тому все робимо в одній транзакції на одному з'єднанні.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // ---------- 1. Ресторани + профілі (1:1) ----------
        var bukovyna = WithId(db, new Restaurant("Буковинська хата", "bukovynska-khata", "Чернівці", "Українська"), 1);
        bukovyna.SetProfile("вул. Ольги Кобилянської, 20", "+380372555001", new TimeOnly(9, 0), new TimeOnly(22, 0),
            150m, "Бануш, борщ і вареники за бабусиними рецептами");

        var pronto = WithId(db, new Restaurant("Pizza Pronto", "pizza-pronto", "Чернівці", "Італійська"), 2);
        pronto.SetProfile("вул. Головна, 45", "+380372555002", new TimeOnly(10, 0), new TimeOnly(23, 0),
            200m, "Неаполітанська піца з дров'яної печі");

        var zen = WithId(db, new Restaurant("Суші Дзен", "sushi-zen", "Чернівці", "Японська"), 3);
        zen.SetProfile("просп. Незалежності, 52", "+380372555003", new TimeOnly(11, 0), new TimeOnly(23, 30),
            300m, null);

        db.Restaurants.AddRange(bukovyna, pronto, zen);
        await InsertWithExplicitIdsAsync(db, "Restaurants", ct);

        // ---------- 2. Страви (1:N) + категорії (M:N з AddedAt / IsFeatured) ----------
        var sep1 = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var sep5 = new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc);
        var sep10 = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc);
        var oct1 = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        var dishes = new List<Dish>
        {
            Dish(db, 101, 1, "Бануш з бринзою і шкварками", 189m, 350, 640, 30, (2, sep1, true), (7, oct1, true)),
            Dish(db, 102, 1, "Борщ з пампушками", 145m, 400, 310, 40, (1, sep1, true), (7, oct1, false)),
            Dish(db, 103, 1, "Вареники з картоплею", 120m, 300, 450, 50, (2, sep1, false), (6, sep1, true)),
            Dish(db, 104, 1, "Узвар 0,5 л", 45m, 500, 120, 100, (5, sep1, false)),
            Dish(db, 201, 2, "Піца Маргарита 30 см", 210m, 450, 980, 25, (3, sep5, true), (6, sep5, false)),
            Dish(db, 202, 2, "Піца Чотири сири 30 см", 265m, 480, 1150, 20, (3, sep5, false), (6, sep5, false)),
            Dish(db, 203, 2, "Лимонад домашній 0,4 л", 60m, 400, 150, 60, (5, sep5, false)),
            Dish(db, 301, 3, "Рол Каліфорнія", 295m, 260, 420, 30, (4, sep10, true)),
            Dish(db, 302, 3, "Рол Філадельфія", 340m, 280, 480, 30, (4, sep10, false), (7, oct1, false)),
            Dish(db, 303, 3, "Місо-суп", 95m, 300, 80, 0, (1, sep10, false))   // 0 порцій → недоступна
        };

        db.Dishes.AddRange(dishes);
        await InsertWithExplicitIdsAsync(db, "Dishes", ct);

        await transaction.CommitAsync(ct);
        logger.LogInformation("Seed completed: {Restaurants} restaurants, {Dishes} dishes", 3, dishes.Count);
    }

    private static Restaurant WithId(MenuDbContext db, Restaurant restaurant, long id)
    {
        // Id має приватний setter; EF вміє записати його сам
        db.Entry(restaurant).Property(r => r.Id).CurrentValue = id;
        return restaurant;
    }

    private static Dish Dish(MenuDbContext db, long id, long restaurantId, string name, decimal price,
        int weight, int calories, int portions, params (int CategoryId, DateTime AddedAt, bool Featured)[] categories)
    {
        var dish = new Dish(restaurantId, name, price, weightGrams: weight, calories: calories);
        db.Entry(dish).Property(d => d.Id).CurrentValue = id;

        if (portions > 0) dish.AddPortions(portions);
        else dish.MarkUnavailable();

        foreach (var (categoryId, addedAt, featured) in categories)
            dish.AddToCategory(categoryId, featured, addedAt);

        return dish;
    }

    private static async Task InsertWithExplicitIdsAsync(MenuDbContext db, string table, CancellationToken ct)
    {
        // назва таблиці — константа з коду, не дані користувача
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync($"SET IDENTITY_INSERT [{table}] ON", ct);
        await db.SaveChangesAsync(ct);
        await db.Database.ExecuteSqlRawAsync($"SET IDENTITY_INSERT [{table}] OFF", ct);
#pragma warning restore EF1002
    }
}
