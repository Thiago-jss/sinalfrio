using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace SinalFrio.Worker;

internal sealed class OutboxRelay(ConnectionFactory factory, OutboxStore store, ILogger<OutboxRelay> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        BrokerLoop.RunAsync(RelayAsync, logger, stoppingToken);

    private async Task RelayAsync(Action healthy, CancellationToken ct)
    {
        await using var connection = await factory.CreateConnectionAsync("sinalfrio-outbox", ct);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), ct);
        await TelemetryTopology.DeclareAsync(channel, ct);

        while (!ct.IsCancellationRequested)
        {
            if (!channel.IsOpen)
                throw new InvalidOperationException("Relay channel closed.");

            // No database reader, transaction, or lock is held while waiting on RabbitMQ.
            var batch = await store.ReadAsync(ct);
            if (batch.Count == 0)
                await Task.Delay(TimeSpan.FromMilliseconds(500), ct);

            foreach (var entry in batch)
            {
                var properties = new BasicProperties
                {
                    Persistent = true,
                    ContentType = "application/json",
                    Type = TelemetryTopology.MessageType,
                    MessageId = entry.Id.ToString("D"),
                    Timestamp = new AmqpTimestamp(new DateTimeOffset(entry.OccurredAt).ToUnixTimeSeconds())
                };
                try
                {
                    using var confirm = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    confirm.CancelAfter(TimeSpan.FromSeconds(5));
                    // With tracking, this await fails for a nack or mandatory return.
                    await channel.BasicPublishAsync("", TelemetryTopology.SourceQueue, mandatory: true,
                        basicProperties: properties, body: Encoding.UTF8.GetBytes(entry.Payload), cancellationToken: confirm.Token);
                    await store.MarkPublishedAsync(entry.Id, ct);
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    logger.LogWarning("Outbox row {MessageId} not confirmed and marked as a unit; it may publish again", entry.Id);
                    throw;
                }
                logger.LogInformation("Relayed outbox row {MessageId}", entry.Id);
            }
            healthy();
        }
    }
}
