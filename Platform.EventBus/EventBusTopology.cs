using RabbitMQ.Client;

namespace Platform.EventBus;

/// <summary>
/// Декларативна топологія брокера (ідемпотентна — виконується при кожному старті сервісу):
///
///   smakoviz.events  (topic, durable) ──routing key "menu.dish.updated"──► delivery-api.events ─┐ x-dead-letter-exchange
///                                     └─ "menu.dish.*" ──────────────────► reviews-api.events  ├──────────► smakoviz.dlx (direct)
///                                     └─ "delivery.order.placed" ────────► menu-api.events    ─┘                 │ routing key = ім'я черги
///                                                                                                     delivery-api.events.dlq, …
///
/// Кожен сервіс-споживач має ВЛАСНУ durable-чергу (подію отримують усі зацікавлені, а не «хто швидший»).
/// Аналогія: topic exchange — сортувальний центр «Нової пошти» за індексом (routing key), черга — поштова скринька
/// сервісу, DLQ — відділ «недоставлених листів», куди потрапляє те, що не вдалося обробити.
/// </summary>
public static class EventBusTopology
{
    public const string Exchange = "smakoviz.events";
    public const string DeadLetterExchange = "smakoviz.dlx";

    public static string DeadLetterQueue(string queue) => $"{queue}.dlq";

    /// <summary>Exchange-и (основний topic + DLX) — оголошує і публікатор, і споживач.</summary>
    public static async Task DeclareExchangesAsync(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: ct);
    }

    /// <summary>Черга сервісу-споживача з DLX + її DLQ + прив'язки за routing keys.</summary>
    public static async Task DeclareConsumerQueueAsync(IChannel channel, string queue, IEnumerable<string> routingKeys,
        CancellationToken ct)
    {
        await DeclareExchangesAsync(channel, ct);

        // DLQ: сюди брокер перекладає Nack(requeue: false) з основної черги
        var dlq = DeadLetterQueue(queue);
        await channel.QueueDeclareAsync(dlq, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(dlq, DeadLetterExchange, routingKey: queue, cancellationToken: ct);

        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = DeadLetterExchange,
                ["x-dead-letter-routing-key"] = queue
            }, cancellationToken: ct);

        foreach (var key in routingKeys.Distinct())
            await channel.QueueBindAsync(queue, Exchange, key, cancellationToken: ct);
    }
}
