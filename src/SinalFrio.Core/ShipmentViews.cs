namespace SinalFrio.Core;

public sealed record ShipmentSummary(Guid Id, string Code, string Route, long ReadingVersion, long EvaluatedVersion, string AssessmentStatus);
public sealed record ShipmentDetail(Guid Id, string Code, string Route, long ReadingVersion, long EvaluatedVersion,
    string AssessmentStatus, decimal TemperatureMin, decimal TemperatureMax, IReadOnlyList<ReadingDto> Readings,
    ExcursionDto? Excursion, ReviewDto? Review);
public sealed record ReadingDto(Guid Id, DateTimeOffset ObservedAt, DateTimeOffset ReceivedAt, decimal Celsius);
public sealed record ExcursionDto(DateTimeOffset StartedAt, DateTimeOffset DetectedAt);
public sealed record ReviewDto(string Decision, string Note, DateTimeOffset ReviewedAt, long EvaluatedVersion);
