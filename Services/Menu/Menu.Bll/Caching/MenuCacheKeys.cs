namespace Menu.Bll.Caching;

/// <summary>
/// Передбачувана схема ключів «сервіс:сутність:id[:варіант]» — префікс menu: ізолює ключі від інших сервісів
/// у спільному Redis, а з id ключ легко інвалідувати після запису.
/// </summary>
public static class MenuCacheKeys
{
    public const string Prefix = "menu:";

    public static string Dish(long id) => $"menu:dish:{id}";

    /// <param name="onlyAvailable">картка ресторану з усіма стравами і лише з доступними — два різні записи</param>
    public static string Restaurant(long id, bool onlyAvailable) => $"menu:restaurant:{id}:{(onlyAvailable ? "available" : "all")}";

    /// <summary>Обидва варіанти картки ресторану — для інвалідації.</summary>
    public static string[] RestaurantAll(long id) => [Restaurant(id, true), Restaurant(id, false)];

    public const string CategoriesAll = "menu:categories:all";
    public static string Category(int id) => $"menu:category:{id}";
}
