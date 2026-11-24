using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;

namespace Platform.EventBus;

/// <summary>Публікація інтеграційних подій. Викликається ЛИШЕ після успішного збереження змін у БД.</summary>
public interface IEventPublisher
{
    /// <param name="routingKey">"&lt;сервіс&gt;.&lt;сутність&gt;.&lt;подія&gt;", напр. "menu.dish.updated".</param>
    Task PublishAsync<TEvent>(TEvent integrationEvent, string routingKey, CancellationToken ct = default)
        where TEvent : IntegrationEvent;
}

/// <summary>
/// RabbitMQ-публікатор. Повідомлення: JSON (camelCase), Persistent (переживе перезапуск брокера в durable-черзі),
/// ContentType, MessageId = EventId, Type = назва події, заголовки x-correlation-id, x-event-version і
/// W3C traceparent (OpenTelemetry Inject) — споживач продовжить ТОЙ САМИЙ trace.
/// Канал один на публікатор (IChannel не потокобезпечний для одночасних publish → SemaphoreSlim).
/// </summary>
public sealed class RabbitMqEventPublisher(IConnection connection, ILogger<RabbitMqEventPublisher> logger)
    : IEventPublisher, IAsyncDisposable
{
    public const string ActivitySourceName = "SmakoViz.EventBus";
    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public const string CorrelationHeader = "x-correlation-id";
    public const string VersionHeader = "x-event-version";
    public const string RetryCountHeader = "x-retry-count";

    private readonly SemaphoreSlim _lock = new(1, 1);
    private IChannel? _channel;

    public async Task PublishAsync<TEvent>(TEvent integrationEvent, string routingKey, CancellationToken ct = default)
        where TEvent : IntegrationEvent
    {
        var eventType = typeof(TEvent).Name;
        var correlationId = integrationEvent.CorrelationId ?? Activity.Current?.TraceId.ToString();

        using var activity = ActivitySource.StartActivity($"{EventBusTopology.Exchange} publish", ActivityKind.Producer);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.operation.type", "publish");
        activity?.SetTag("messaging.destination.name", EventBusTopology.Exchange);
        activity?.SetTag("messaging.rabbitmq.destination.routing_key", routingKey);
        activity?.SetTag("messaging.message.id", integrationEvent.EventId.ToString());
        activity?.SetTag("messaging.message.type", eventType);

        var body = JsonSerializer.SerializeToUtf8Bytes(integrationEvent, EventJson.Options);
        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = integrationEvent.EventId.ToString(),
            Type = eventType,
            Timestamp = new AmqpTimestamp(new DateTimeOffset(integrationEvent.OccurredAt).ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?>
            {
                [CorrelationHeader] = correlationId,
                [VersionHeader] = integrationEvent.Version
            }
        };

        // W3C trace context → заголовки (traceparent / tracestate)
        Propagators.DefaultTextMapPropagator.Inject(
            new PropagationContext(activity?.Context ?? Activity.Current?.Context ?? default, Baggage.Current),
            properties.Headers, (headers, key, value) => headers![key] = Encoding.UTF8.GetBytes(value));

        await _lock.WaitAsync(ct);
        try
        {
            _channel ??= await CreateChannelAsync(ct);
            await _channel.BasicPublishAsync(EventBusTopology.Exchange, routingKey, mandatory: false,
                basicProperties: properties, body: body, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "Failed to publish {EventType} {EventId} with {RoutingKey} (CorrelationId {CorrelationId})",
                eventType, integrationEvent.EventId, routingKey, correlationId);
            _channel = null;   // наступна спроба створить новий канал
            throw;
        }
        finally
        {
            _lock.Release();
        }

        logger.LogInformation("Published {EventType} {EventId} to {Exchange} with {RoutingKey}, {SizeBytes} bytes (CorrelationId {CorrelationId})",
            eventType, integrationEvent.EventId, EventBusTopology.Exchange, routingKey, body.Length, correlationId);
    }

    private async Task<IChannel> CreateChannelAsync(CancellationToken ct)
    {
        var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        await EventBusTopology.DeclareExchangesAsync(channel, ct);
        return channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
            await _channel.DisposeAsync();
        _lock.Dispose();
    }
}
