using System.Diagnostics.Metrics;
using Delivery.Domain.Models;

namespace Delivery.Api.Metrics;

/// <summary>
/// Бізнес-метрики сервісу «Доставка» (Лаб. 07). Singleton; Meter "Delivery.Api" (= назва сервісу) підключено
/// в ServiceDefaults через AddMeter. Аналогія: лічильник відвідувачів на вході (Counter — лише зростає),
/// секундомір касира (Histogram — розподіл тривалостей) і табло «зараз у роботі N замовлень» (ObservableGauge — знімок).
///
/// Теги — лише з НИЗЬКОЮ кардинальністю (однакові назви в усіх сервісах):
///   order.status  — Placed | Confirmed | Cooking | OnTheWay | Delivered | Cancelled | Paid (7 значень);
///   order.result  — created | replayed (повтор з тим самим Idempotency-Key);
///   operation     — place | get | confirm | cancel | change_status | add_line | delete;
///   status        — success | failure.
/// Жодних OrderId / CustomerId у тегах: кожне унікальне значення = окремий часовий ряд.
/// </summary>
public sealed class DeliveryMetrics
{
    public const string MeterName = "Delivery.Api";

    private readonly Counter<long> _ordersCreated;
    private readonly Counter<long> _ordersConfirmed;
    private readonly Counter<long> _ordersCancelled;
    private readonly Counter<long> _statusChanges;
    private readonly Counter<decimal> _revenue;
    private readonly Histogram<double> _operationDuration;
    private readonly Histogram<int> _orderItems;

    public DeliveryMetrics(IMeterFactory meterFactory, OrdersSnapshot snapshot)
    {
        var meter = meterFactory.Create(MeterName);

        _ordersCreated = meter.CreateCounter<long>("orders.created", unit: "{order}", description: "Number of created orders");
        _ordersConfirmed = meter.CreateCounter<long>("orders.confirmed", unit: "{order}", description: "Orders confirmed by restaurants");
        _ordersCancelled = meter.CreateCounter<long>("orders.cancelled", unit: "{order}", description: "Cancelled orders");
        _statusChanges = meter.CreateCounter<long>("orders.status_changed", unit: "{order}", description: "Order status transitions by target status");
        _revenue = meter.CreateCounter<decimal>("orders.revenue", unit: "UAH", description: "Total amount of created orders");
        _operationDuration = meter.CreateHistogram<double>("orders.operation.duration", unit: "ms",
            description: "Duration of order operations (successful AND failed)");
        _orderItems = meter.CreateHistogram<int>("orders.items", unit: "{item}", description: "Number of lines in a created order");

        // Gauge — ПОТОЧНИЙ стан (значення читається callback-ом у момент збору метрик), по одному ряду на статус
        meter.CreateObservableGauge("orders.active", () => snapshot.ActiveByStatus
                .Select(kv => new Measurement<long>(kv.Value, new KeyValuePair<string, object?>("order.status", kv.Key))),
            unit: "{order}", description: "Orders in progress (not delivered / cancelled) by status");
    }

    public void OrderCreated(string status, decimal totalAmount, int itemsCount, bool replayed)
    {
        var statusTag = new KeyValuePair<string, object?>("order.status", status);
        _ordersCreated.Add(1, statusTag, new KeyValuePair<string, object?>("order.result", replayed ? "replayed" : "created"));
        if (replayed) return;
        _revenue.Add(totalAmount);
        _orderItems.Record(itemsCount);
    }

    public void OrderConfirmed() => _ordersConfirmed.Add(1);
    public void OrderCancelled() => _ordersCancelled.Add(1);

    public void StatusChanged(string toStatus)
        => _statusChanges.Add(1, new KeyValuePair<string, object?>("order.status",
            OrderStatus.IsKnown(toStatus) ? toStatus : "unknown"));   // захист кардинальності від довільних рядків

    public void RecordDuration(string operation, bool success, double ms)
        => _operationDuration.Record(ms,
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("status", success ? "success" : "failure"));
}

/// <summary>Знімок кількості активних замовлень за статусами — оновлює OrdersSnapshotRefresher, читає gauge.</summary>
public sealed class OrdersSnapshot
{
    public static readonly string[] ActiveStatuses =
        [OrderStatus.Placed, OrderStatus.Confirmed, OrderStatus.Paid, OrderStatus.Cooking, OrderStatus.OnTheWay];

    private volatile IReadOnlyDictionary<string, long> _activeByStatus = new Dictionary<string, long>();

    public IReadOnlyDictionary<string, long> ActiveByStatus => _activeByStatus;

    public void Update(IReadOnlyDictionary<string, long> values) => _activeByStatus = values;
}
