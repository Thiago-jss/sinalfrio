using Microsoft.EntityFrameworkCore;
using SinalFrio.Contracts;
using SinalFrio.Core;

namespace SinalFrio.Infrastructure;

internal sealed class ShipmentEvaluator(SinalFrioDbContext database) : IShipmentEvaluator
{
    public async Task EvaluateAsync(TemperatureReadingAcceptedV1 message, CancellationToken ct)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(ct);
        var shipment = await database.LockShipmentAsync(message.ShipmentId, ct)
            ?? throw new InvalidDataException("Shipment does not exist.");
        if (message.ReadingVersion <= 0 || message.ReadingVersion > shipment.ReadingVersion || message.MessageId == Guid.Empty)
            throw new InvalidDataException("Invalid reading acceptance event.");

        var inserted = await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.sf_inbox (message_id) VALUES ({message.MessageId})
            ON CONFLICT (message_id) DO NOTHING
            """, ct);
        if (inserted == 0) return;

        if (shipment.EvaluatedVersion < shipment.ReadingVersion)
        {
            var samples = await database.Readings.Where(r => r.ShipmentId == shipment.Id)
                .Select(r => new ThermalSample(r.ObservedAt, r.Celsius)).ToListAsync(ct);
            var assessment = ThermalPolicy.Assess(samples, shipment.TemperatureMin, shipment.TemperatureMax);
            await database.Shipments.Where(s => s.Id == shipment.Id).ExecuteUpdateAsync(set => set
                .SetProperty(s => s.Assessment, assessment.Status)
                .SetProperty(s => s.ExcursionStartedAt, assessment.StartedAt)
                .SetProperty(s => s.ExcursionDetectedAt, assessment.DetectedAt)
                .SetProperty(s => s.EvaluatedVersion, shipment.ReadingVersion), ct);
        }
        await transaction.CommitAsync(ct);
    }
}
