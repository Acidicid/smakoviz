using System.ComponentModel.DataAnnotations;

namespace Delivery.Bll.Dtos;

public record DishDto(
    long Id,
    long RestaurantId,
    string Name,
    decimal Price,
    bool IsAvailable,
    DateTime SyncedAt);

/// <summary>
/// Синхронізація локальної копії страви з Меню (PUT /api/dishes/{id}).
/// У Лаб. 08 це робитиме обробник події DishChanged, а поки — службовий ендпоінт.
/// </summary>
public class UpsertDishDto
{
    [Range(1, long.MaxValue)]
    public long RestaurantId { get; set; }

    [Required, StringLength(150, MinimumLength = 1)]
    public string Name { get; set; } = "";

    [Range(typeof(decimal), "0.01", "100000")]
    public decimal Price { get; set; }

    public bool IsAvailable { get; set; } = true;
}
