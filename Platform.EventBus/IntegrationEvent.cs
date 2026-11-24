using System.Text.Json;

namespace Platform.EventBus;

/// <summary>
/// Базова інтеграційна подія: НЕЗМІННИЙ record, назва — у минулому часі (DishUpdated, OrderPlaced),
/// дані самодостатні — споживачу не треба нічого дозапитувати в сервісу-власника.
/// Аналогія: це не дзвінок «зайди, щось покажу», а лист із вкладенням — усе потрібне вже всередині.
/// </summary>
public abstract record IntegrationEvent
{
    /// <summary>Унікальний id події: MessageId повідомлення і ключ ідемпотентності в споживачів.</summary>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>Коли подія сталася (UTC).</summary>
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

    /// <summary>Наскрізний CorrelationId (Лаб. 05) — щоб знайти в логах ВЕСЬ ланцюжок: HTTP-запит → подія → обробка.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Версія контракту. Споживач відкидає (у DLQ) версії, яких не розуміє.</summary>
    public int Version { get; init; } = 1;
}

/// <summary>Спільні налаштування JSON для подій: camelCase, як і в REST API.</summary>
public static class EventJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

/// <summary>Постійна помилка обробки (повтор не допоможе) → повідомлення одразу в DLQ.</summary>
public sealed class PermanentEventException(string message, Exception? inner = null) : Exception(message, inner);
