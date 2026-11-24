using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Platform.EventBus;

/// <summary>Обробник одного типу події. Створюється в окремому DI-scope на КОЖНЕ повідомлення.</summary>
public interface IIntegrationEventHandler<in TEvent> where TEvent : IntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken ct);
}

/// <summary>Налаштування споживача сервісу: черга, routing keys → тип події + обробник.</summary>
public sealed class EventConsumerOptions
{
    public string QueueName { get; set; } = "";
    public ushort PrefetchCount { get; set; } = 10;
    public int MaxRetryAttempts { get; set; } = 3;
    public int SupportedVersion { get; set; } = 1;

    internal Dictionary<string, Subscription> Subscriptions { get; } = new(StringComparer.Ordinal);

    /// <summary>Підписка: routing key → (тип події, виклик обробника в scope).</summary>
    public EventConsumerOptions Subscribe<TEvent, THandler>(string routingKey)
        where TEvent : IntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        Subscriptions[routingKey] = new Subscription(typeof(TEvent), typeof(THandler),
            (sp, evt, ct) => sp.GetRequiredService<THandler>().HandleAsync((TEvent)evt, ct));
        return this;
    }

    internal sealed record Subscription(Type EventType, Type HandlerType,
        Func<IServiceProvider, IntegrationEvent, CancellationToken, Task> Invoke);
}

/// <summary>
/// Споживач подій як BackgroundService. Ручне підтвердження (autoAck: false), BasicQos (prefetch),
/// AsyncEventingBasicConsumer, коректна зупинка за stoppingToken. Рішення для кожного повідомлення:
///   успіх                                   → Ack;
///   некоректний JSON / несумісна версія /
///   невідомий тип / PermanentEventException → Nack(requeue: false) → одразу DLQ;
///   тимчасовий збій (БД недоступна тощо)    → перепублікація в ту ж чергу з x-retry-count + 1 і Ack оригіналу;
///                                              після MaxRetryAttempts → DLQ.
/// Аналогія: сортувальник листів. Розібрав лист — ставить галочку (Ack). Лист нечитабельний — одразу у відділ
/// «недоставлених» (DLQ). Адресата немає вдома — кладе лист назад у стос з позначкою «спроба N»; після 3-ї — теж у DLQ.
/// </summary>
public sealed class RabbitMqEventConsumer(
    IConnection connection,
    IServiceScopeFactory scopeFactory,
    EventConsumerOptions options,
    ILogger<RabbitMqEventConsumer> logger) : BackgroundService
{
    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await EventBusTopology.DeclareConsumerQueueAsync(_channel, options.QueueName, options.Subscriptions.Keys, stoppingToken);
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: options.PrefetchCount, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += (_, args) => HandleDeliveryAsync(args, stoppingToken);

        await _channel.BasicConsumeAsync(options.QueueName, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);
        logger.LogInformation("Consuming {Queue}: {RoutingKeys}, prefetch {Prefetch}",
            options.QueueName, string.Join(", ", options.Subscriptions.Keys), options.PrefetchCount);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // штатна зупинка застосунку
        }
    }

    private async Task HandleDeliveryAsync(BasicDeliverEventArgs args, CancellationToken stoppingToken)
    {
        var channel = _channel!;
        var headers = args.BasicProperties.Headers ?? new Dictionary<string, object?>();
        var correlationId = HeaderString(headers, RabbitMqEventPublisher.CorrelationHeader);
        var retryCount = HeaderInt(headers, RabbitMqEventPublisher.RetryCountHeader);
        var messageId = args.BasicProperties.MessageId;

        // W3C traceparent із заголовків → батьківський контекст: trace продовжується від публікатора
        var parent = Propagators.DefaultTextMapPropagator.Extract(default, headers,
            (h, key) => h.TryGetValue(key, out var v) && v is byte[] bytes
                ? new[] { Encoding.UTF8.GetString(bytes) }
                : Array.Empty<string>());
        Baggage.Current = parent.Baggage;

        using var activity = RabbitMqEventPublisher.ActivitySource.StartActivity(
            $"{options.QueueName} process", ActivityKind.Consumer, parent.ActivityContext);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.operation.type", "process");
        activity?.SetTag("messaging.destination.name", options.QueueName);
        activity?.SetTag("messaging.rabbitmq.destination.routing_key", args.RoutingKey);
        activity?.SetTag("messaging.message.id", messageId);
        activity?.SetTag("messaging.message.type", args.BasicProperties.Type);
        activity?.SetTag("messaging.retry_count", retryCount);

        using var scopeLog = logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["EventId"] = messageId,
            ["EventType"] = args.BasicProperties.Type,
            ["RoutingKey"] = args.RoutingKey
        });

        // ---------- постійні помилки → одразу DLQ ----------
        if (!options.Subscriptions.TryGetValue(args.RoutingKey, out var subscription))
        {
            await DeadLetterAsync(channel, args, "no handler for routing key", null, stoppingToken);
            return;
        }

        var version = HeaderInt(headers, RabbitMqEventPublisher.VersionHeader, defaultValue: 1);
        if (version > options.SupportedVersion)
        {
            await DeadLetterAsync(channel, args, $"unsupported event version {version}", null, stoppingToken);
            return;
        }

        IntegrationEvent? integrationEvent;
        try
        {
            integrationEvent = (IntegrationEvent?)JsonSerializer.Deserialize(args.Body.Span, subscription.EventType, EventJson.Options);
            if (integrationEvent is null) throw new JsonException("Empty message body");
        }
        catch (JsonException ex)
        {
            await DeadLetterAsync(channel, args, "invalid JSON", ex, stoppingToken);
            return;
        }

        // ---------- обробка в окремому scope ----------
        var sw = Stopwatch.StartNew();
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await subscription.Invoke(scope.ServiceProvider, integrationEvent, stoppingToken);

            await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
            logger.LogInformation("Handled {EventType} {EventId} by {Handler} in {ElapsedMs} ms (attempt {Attempt})",
                subscription.EventType.Name, integrationEvent.EventId, subscription.HandlerType.Name, sw.ElapsedMilliseconds, retryCount + 1);
        }
        catch (PermanentEventException ex)
        {
            await DeadLetterAsync(channel, args, ex.Message, ex, stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            if (retryCount + 1 >= options.MaxRetryAttempts)
            {
                await DeadLetterAsync(channel, args, $"failed after {retryCount + 1} attempts", ex, stoppingToken);
                return;
            }

            await RetryAsync(channel, args, headers, retryCount + 1, ex, stoppingToken);
        }
    }

    /// <summary>
    /// Повтор: невелика експоненційна пауза, копія повідомлення з x-retry-count + 1 у ту саму чергу
    /// (через default exchange — побачить лише цей сервіс), оригінал підтверджується.
    /// </summary>
    private async Task RetryAsync(IChannel channel, BasicDeliverEventArgs args, IDictionary<string, object?> headers,
        int nextRetry, Exception ex, CancellationToken ct)
    {
        logger.LogWarning(ex, "Transient failure handling {EventType} {EventId}; retry {RetryCount}/{MaxRetries}",
            args.BasicProperties.Type, args.BasicProperties.MessageId, nextRetry, options.MaxRetryAttempts - 1);

        await Task.Delay(TimeSpan.FromMilliseconds(500 * Math.Pow(2, nextRetry - 1)), ct);

        var properties = new BasicProperties(args.BasicProperties)
        {
            Headers = new Dictionary<string, object?>(headers) { [RabbitMqEventPublisher.RetryCountHeader] = nextRetry }
        };
        await channel.BasicPublishAsync(exchange: "", routingKey: options.QueueName, mandatory: false,
            basicProperties: properties, body: args.Body, cancellationToken: ct);
        await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken: ct);
    }

    private async Task DeadLetterAsync(IChannel channel, BasicDeliverEventArgs args, string reason, Exception? ex, CancellationToken ct)
    {
        logger.LogError(ex, "Message {EventId} ({EventType}, {RoutingKey}) sent to DLQ {DeadLetterQueue}: {Reason}",
            args.BasicProperties.MessageId, args.BasicProperties.Type, args.RoutingKey,
            EventBusTopology.DeadLetterQueue(options.QueueName), reason);
        await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, cancellationToken: ct);
    }

    private static string? HeaderString(IDictionary<string, object?> headers, string key)
        => headers.TryGetValue(key, out var value) ? value switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string s => s,
            _ => value?.ToString()
        } : null;

    private static int HeaderInt(IDictionary<string, object?> headers, string key, int defaultValue = 0)
        => headers.TryGetValue(key, out var value) ? value switch
        {
            int i => i,
            long l => (int)l,
            byte b => b,
            byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) => parsed,
            _ => defaultValue
        } : defaultValue;

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_channel is not null)
        {
            await _channel.CloseAsync(cancellationToken);
            await _channel.DisposeAsync();
        }
    }
}

public static class EventBusServiceCollectionExtensions
{
    /// <summary>Публікатор подій (Singleton: один канал на процес). IConnection реєструє AddRabbitMQClient("messaging").</summary>
    public static IServiceCollection AddEventPublisher(this IServiceCollection services)
        => services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();

    /// <summary>Споживач: черга сервісу + підписки. Обробники реєструються як Scoped.</summary>
    public static IServiceCollection AddEventConsumer(this IServiceCollection services, string queueName,
        Action<EventConsumerOptions> configure)
    {
        var options = new EventConsumerOptions { QueueName = queueName };
        configure(options);
        foreach (var subscription in options.Subscriptions.Values)
            services.AddScoped(subscription.HandlerType);

        services.AddSingleton(options);
        services.AddHostedService<RabbitMqEventConsumer>();
        return services;
    }
}
