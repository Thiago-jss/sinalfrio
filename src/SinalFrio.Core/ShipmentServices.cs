using SinalFrio.Contracts;

namespace SinalFrio.Core;

public interface IShipmentEvaluator
{
    Task EvaluateAsync(TemperatureReadingAcceptedV1 message, CancellationToken ct);
}

public interface IShipmentService
{
    Task<ShipmentSummary[]> ListAsync(Guid organizationId, CancellationToken ct);
    Task<ShipmentDetail?> GetAsync(Guid organizationId, Guid shipmentId, CancellationToken ct);
    Task<WriteResult> IngestAsync(SensorActor actor, Guid shipmentId, Guid readingId, DateTimeOffset observedAt, decimal celsius, CancellationToken ct);
    Task<WriteResult> ReviewAsync(OperatorActor actor, Guid shipmentId, long expectedVersion, string decision, string note, CancellationToken ct);
}

public sealed record SensorActor(string Id, Guid OrganizationId, Guid ShipmentId);
public sealed record OperatorActor(string Id, Guid OrganizationId);
public enum WriteStatus { Success, Invalid, Forbidden, NotFound, Conflict }
public sealed record WriteResult(WriteStatus Status, string? Reason = null, ShipmentDetail? Detail = null);

public static class DemoScope
{
    public static readonly Guid OrganizationId = Guid.Parse("00000000-0000-4000-8000-000000000010");
    public static readonly Guid PrimaryShipmentId = Guid.Parse("00000000-0000-4000-8000-000000000001");
    public static readonly Guid SecondaryShipmentId = Guid.Parse("00000000-0000-4000-8000-000000000002");
    public const string SensorId = "demo-sensor";
    public const string OperatorId = "demo-operator";
}
