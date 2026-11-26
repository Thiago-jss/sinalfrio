namespace SinalFrio.Contracts;

public sealed record TemperatureReadingAcceptedV1(Guid MessageId, Guid ShipmentId, long ReadingVersion);
