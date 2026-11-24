namespace Delivery.Bll.Caching;

/// <summary>Ключі з префіксом delivery: — ізоляція від інших сервісів у спільному Redis.</summary>
public static class DeliveryCacheKeys
{
    public static string Order(long id) => $"delivery:order:{id}";
    public static string Dish(long id) => $"delivery:dish:{id}";
    public static string RestaurantDishes(long restaurantId, bool onlyAvailable)
        => $"delivery:restaurant:{restaurantId}:dishes:{(onlyAvailable ? "available" : "all")}";
    public static string[] RestaurantDishesAll(long restaurantId)
        => [RestaurantDishes(restaurantId, true), RestaurantDishes(restaurantId, false)];
}
