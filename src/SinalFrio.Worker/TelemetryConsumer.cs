using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SinalFrio.Contracts;
using SinalFrio.Core;

namespace SinalFrio.Worker;

internal sealed class TelemetryConsumer(ConnectionFactory factory, IServiceScopeFactory scopes,
    ILogger<TelemetryConsumer> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true
    };
    private const int MaxAttempts = 3;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        BrokerLoop.RunAsync(ConsumeAsync, logger, stoppingToken);

    private async Task ConsumeAsync(Action healthy, CancellationToken ct)
    {
        await using var connection = await factory.CreateConnectionAsync("sinalfrio-consumer", ct);
        using var session = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        var terminated = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task EndSession(Exception error)
        {
            terminated.TrySetResult(error);
            return Task.CompletedTask;
        }

        channel.ChannelShutdownAsync += (_, _) => EndSession(new InvalidOperationException("Consumer channel closed."));
        await TelemetryTopology.DeclareAsync(channel, ct);
        await channel.BasicQosAsync(0, 1, global: false, cancellationToken: ct);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.UnregisteredAsync += (_, _) => EndSession(new InvalidOperationException("Consumer unregistered."));
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            try { await HandleAsync(channel, delivery, session.Token); }
            catch (OperationCanceledException) when (session.IsCancellationRequested) { }
            catch (Exception error) { await EndSession(error); }
        };

        try
        {
            await channel.BasicConsumeAsync(TelemetryTopology.SourceQueue, autoAck: false, consumer: consumer, cancellationToken: ct);
            logger.LogInformation("Consuming queue {Queue}", TelemetryTopology.SourceQueue);
            // Reset backoff only after a live session lasts long enough, not during a restart loop.
            if (await Task.WhenAny(terminated.Task, Task.Delay(TimeSpan.FromSeconds(10), ct)) != terminated.Task)
            {
                ct.ThrowIfCancellationRequested();
                healthy();
            }
            throw await terminated.Task.WaitAsync(ct);
        }
        finally { await session.CancelAsync(); }
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        using var handling = CancellationTokenSource.CreateLinkedTokenSource(ct, delivery.CancellationToken);
        ct = handling.Token;
        ct.ThrowIfCancellationRequested();
        TemperatureReadingAcceptedV1? message = null;
        try { message = JsonSerializer.Deserialize<TemperatureReadingAcceptedV1>(delivery.Body.Span, Json); }
        catch (JsonException) { }

        if (message is null || message.MessageId == Guid.Empty || message.ShipmentId == Guid.Empty || message.ReadingVersion <= 0)
        {
            logger.LogWarning("Rejecting malformed delivery {DeliveryTag}", delivery.DeliveryTag);
            ct.ThrowIfCancellationRequested();
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, cancellationToken: ct);
            return;
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IShipmentEvaluator>().EvaluateAsync(message, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                ct.ThrowIfCancellationRequested();
                if (attempt < MaxAttempts && IsTransient(error))
                {
                    logger.LogWarning("Retrying evaluation {MessageId} for shipment {ShipmentId} after attempt {Attempt} ({FailureType})",
                        message.MessageId, message.ShipmentId, attempt, error.GetType().Name);
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), ct);
                    continue;
                }

                logger.LogError("Dead-lettering evaluation {MessageId} for shipment {ShipmentId} after {Attempt} attempt(s) ({FailureType})",
                    message.MessageId, message.ShipmentId, attempt, error.GetType().Name);
                ct.ThrowIfCancellationRequested();
                // Quorum at-least-once dead-lettering retains this message until the DLQ confirms it.
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, cancellationToken: ct);
                return;
            }

            // Transport settlement failure is not an evaluator failure; reconnect and allow redelivery.
            ct.ThrowIfCancellationRequested();
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken: ct);
            logger.LogInformation("Settled evaluation {MessageId} for shipment {ShipmentId}", message.MessageId, message.ShipmentId);
            return;
        }
    }

    private static bool IsTransient(Exception error)
    {
        // The persistence implementation can wrap a provider exception (e.g. DbUpdateException).
        for (Exception? cause = error; cause is not null; cause = cause.InnerException)
            if (cause is NpgsqlException { IsTransient: true } or TimeoutException)
                return true;
        return false;
    }
}
