namespace Delivery.Domain.Models;

/// <summary>
/// Локальна КОПІЯ страви з контексту «Меню» (dbo.Dishes у SmakoViz_Delivery).
/// Id збігається з Id у БД Меню. Власник назви й ціни — Меню; тут дані лише читаються
/// і синхронізуються (у Лаб. 08 — подіями).
/// </summary>
public class Dish
{
    public long Id { get; set; }
    public long RestaurantId { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public bool IsAvailable { get; set; }
    public DateTime SyncedAt { get; set; }
}
