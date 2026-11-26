using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SinalFrio.Contracts;
using SinalFrio.Core;

namespace SinalFrio.Infrastructure;

internal sealed class ShipmentService(SinalFrioDbContext database, TimeProvider clock) : IShipmentService
{
    private const int ReadingLimit = 2000;
    private static readonly JsonSerializerOptions EventJson = new(JsonSerializerDefaults.Web);

    public async Task<ShipmentSummary[]> ListAsync(Guid organizationId, CancellationToken ct)
    {
        var shipments = await database.Shipments.AsNoTracking().Where(s => s.OrganizationId == organizationId)
            .OrderBy(s => s.Code).ToListAsync(ct);
        return shipments.Select(s => new ShipmentSummary(s.Id, s.Code, s.Route, s.ReadingVersion,
            s.EvaluatedVersion, WireStatus(s))).ToArray();
    }

    public async Task<ShipmentDetail?> GetAsync(Guid organizationId, Guid shipmentId, CancellationToken ct)
    {
        await using var snapshot = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var shipment = await database.Shipments.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == shipmentId && s.OrganizationId == organizationId, ct);
        var detail = shipment is null ? null : await ReadDetailAsync(shipment, ct);
        await snapshot.CommitAsync(ct);
        return detail;
    }

    public async Task<WriteResult> IngestAsync(SensorActor actor, Guid shipmentId, Guid readingId,
        DateTimeOffset observedAt, decimal celsius, CancellationToken ct)
    {
        if (actor.ShipmentId != shipmentId) return new(WriteStatus.Forbidden);
        var now = clock.GetUtcNow();
        if (!ReadingPolicy.TryNormalize(readingId, observedAt, celsius, now, out var sample))
            return new(WriteStatus.Invalid, "Use a nonempty reading ID, a valid observation no more than five minutes ahead, and -80 to 80 °C.");

        await using var transaction = await database.Database.BeginTransactionAsync(ct);
        var shipment = await database.LockShipmentAsync(shipmentId, ct);
        if (shipment is null || shipment.OrganizationId != actor.OrganizationId) return new(WriteStatus.NotFound);

        var existing = await database.Readings.AsNoTracking().SingleOrDefaultAsync(
            r => r.ShipmentId == shipmentId && r.Id == readingId, ct);
        if (existing is not null)
            return existing.SubmittedUtcTicks == observedAt.UtcTicks && existing.SubmittedCelsius == celsius && existing.SensorId == actor.Id
                ? new(WriteStatus.Success)
                : new(WriteStatus.Conflict, "This reading ID was accepted with different content.");

        if (shipment.ReadingVersion >= ReadingLimit)
            return new(WriteStatus.Conflict, "This shipment has reached its reading limit.");
        if (await database.Readings.AnyAsync(r => r.ShipmentId == shipmentId && r.ObservedAt == sample.ObservedAt, ct))
            return new(WriteStatus.Conflict, "A different reading already occupies this observation timestamp.");

        var version = shipment.ReadingVersion + 1;
        await database.Shipments.Where(s => s.Id == shipmentId)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.ReadingVersion, version), ct);
        database.Readings.Add(new Reading
        {
            ShipmentId = shipmentId, Id = readingId, ObservedAt = sample.ObservedAt, ReceivedAt = now,
            Celsius = sample.Celsius, SensorId = actor.Id, SubmittedUtcTicks = observedAt.UtcTicks, SubmittedCelsius = celsius
        });
        var message = new TemperatureReadingAcceptedV1(Guid.NewGuid(), shipmentId, version);
        database.Outbox.Add(new OutboxEntry
        {
            Id = message.MessageId, Payload = JsonSerializer.Serialize(message, EventJson), OccurredAt = now
        });
        await database.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(WriteStatus.Success);
    }

    public async Task<WriteResult> ReviewAsync(OperatorActor actor, Guid shipmentId, long expectedVersion,
        string decision, string note, CancellationToken ct)
    {
        if (decision != "acknowledged" || string.IsNullOrWhiteSpace(note) || note.Length > 1000 || note.Contains('\0'))
            return new(WriteStatus.Invalid, "Use the acknowledged decision and a nonblank note of at most 1000 characters.");

        await using var transaction = await database.Database.BeginTransactionAsync(ct);
        var shipment = await database.LockShipmentAsync(shipmentId, ct);
        if (shipment is null || shipment.OrganizationId != actor.OrganizationId) return new(WriteStatus.NotFound);
        if (shipment.ReadingVersion != shipment.EvaluatedVersion || expectedVersion != shipment.EvaluatedVersion ||
            shipment.Assessment != AssessmentStatus.Excursion)
            return new(WriteStatus.Conflict, "A review requires the current evaluated version and a confirmed excursion.");
        if (await database.Reviews.AnyAsync(r => r.ShipmentId == shipmentId && r.EvaluatedVersion == expectedVersion, ct))
            return new(WriteStatus.Conflict, "This evaluated version has already been reviewed.");

        database.Reviews.Add(new Review
        {
            ShipmentId = shipmentId, EvaluatedVersion = expectedVersion, Decision = decision,
            Note = note, ReviewedAt = clock.GetUtcNow(), OperatorId = actor.Id
        });
        await database.SaveChangesAsync(ct);
        var detail = await ReadDetailAsync(shipment, ct);
        await transaction.CommitAsync(ct);
        return new(WriteStatus.Success, Detail: detail);
    }

    private async Task<ShipmentDetail> ReadDetailAsync(Shipment shipment, CancellationToken ct)
    {
        var readings = await database.Readings.Where(r => r.ShipmentId == shipment.Id).OrderBy(r => r.ObservedAt)
            .Select(r => new ReadingDto(r.Id, r.ObservedAt, r.ReceivedAt, r.Celsius)).ToListAsync(ct);
        var review = await database.Reviews.Where(r => r.ShipmentId == shipment.Id).OrderByDescending(r => r.EvaluatedVersion)
            .Select(r => new ReviewDto(r.Decision, r.Note, r.ReviewedAt, r.EvaluatedVersion)).FirstOrDefaultAsync(ct);
        var excursion = shipment.ExcursionStartedAt is { } started && shipment.ExcursionDetectedAt is { } detected
            ? new ExcursionDto(started, detected) : null;
        return new(shipment.Id, shipment.Code, shipment.Route, shipment.ReadingVersion, shipment.EvaluatedVersion,
            WireStatus(shipment), shipment.TemperatureMin, shipment.TemperatureMax, readings, excursion, review);
    }

    private static string WireStatus(Shipment shipment) => shipment.EvaluatedVersion != shipment.ReadingVersion
        ? "pending" : shipment.Assessment switch
        {
            AssessmentStatus.Pending => "pending",
            AssessmentStatus.InsufficientEvidence => "insufficientEvidence",
            AssessmentStatus.WithinRange => "withinRange",
            AssessmentStatus.Excursion => "excursion",
            _ => throw new InvalidDataException("Unknown assessment status.")
        };
}
