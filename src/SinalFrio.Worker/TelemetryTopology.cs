using RabbitMQ.Client;

namespace SinalFrio.Worker;

internal static class TelemetryTopology
{
    internal const string SourceQueue = "sinalfrio.telemetry.v1";
    internal const string DeadQueue = "sinalfrio.telemetry.dead";
    internal const string MessageType = "TemperatureReadingAcceptedV1";
    private const string DeadExchange = "sinalfrio.telemetry.dlx";

    internal static async Task DeclareAsync(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(DeadExchange, ExchangeType.Direct, durable: true, cancellationToken: ct);
        await channel.QueueDeclareAsync(DeadQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum", ["x-delivery-limit"] = -1 }, cancellationToken: ct);
        await channel.QueueBindAsync(DeadQueue, DeadExchange, DeadQueue, cancellationToken: ct);
        await channel.QueueDeclareAsync(SourceQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-dead-letter-exchange"] = DeadExchange,
                ["x-dead-letter-routing-key"] = DeadQueue,
                ["x-dead-letter-strategy"] = "at-least-once",
                ["x-overflow"] = "reject-publish",
                ["x-delivery-limit"] = -1 // Session loss/shutdown is not a processing rejection.
            }, cancellationToken: ct);
    }
}