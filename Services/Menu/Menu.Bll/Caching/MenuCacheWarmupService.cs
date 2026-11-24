using Menu.Bll.Dtos;
using Menu.Bll.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Menu.Bll.Caching;

/// <summary>
/// Прогрівання кешу: при старті й далі кожні 10 хв завантажує найчастіше запитувані дані
/// (довідник категорій і картки активних ресторанів) — перший клієнт не чекає на БД.
/// Помилки лише логуються: прогрів — оптимізація, а не умова роботи сервісу.
/// </summary>
public sealed class MenuCacheWarmupService(IServiceScopeFactory scopeFactory, ILogger<MenuCacheWarmupService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var categories = scope.ServiceProvider.GetRequiredService<ICategoryService>();
                var restaurants = scope.ServiceProvider.GetRequiredService<IRestaurantService>();

                var loadedCategories = await categories.GetAllAsync(stoppingToken);
                var page = await restaurants.GetListAsync(new RestaurantQuery { Page = 1, PageSize = 20 }, stoppingToken);
                foreach (var restaurant in page.Items)
                    await restaurants.GetByIdAsync(restaurant.Id, onlyAvailableDishes: true, stoppingToken);

                logger.LogInformation("Cache warmed up: {CategoriesCount} categories, {RestaurantsCount} restaurants",
                    loadedCategories.Count, page.Items.Count);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Cache warm-up failed; will retry in {Interval}", Interval);
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
