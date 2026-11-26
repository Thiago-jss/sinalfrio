namespace SinalFrio.Core;

public enum AssessmentStatus { Pending, InsufficientEvidence, WithinRange, Excursion }
public readonly record struct ThermalSample(DateTimeOffset ObservedAt, decimal Celsius);
public readonly record struct ThermalAssessment(AssessmentStatus Status, DateTimeOffset? StartedAt = null, DateTimeOffset? DetectedAt = null);

public static class ThermalPolicy
{
    private static readonly TimeSpan MaximumGap = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ExcursionDuration = TimeSpan.FromMinutes(10);

    public static ThermalAssessment Assess(IEnumerable<ThermalSample> samples, decimal min, decimal max)
    {
        DateTimeOffset? previous = null;
        DateTimeOffset? outsideSince = null;
        var previousSide = 0;
        var count = 0;
        var anyGap = false;
        var anyOutside = false;

        foreach (var sample in samples.OrderBy(sample => sample.ObservedAt))
        {
            count++;
            var gap = previous is { } last && sample.ObservedAt - last > MaximumGap;
            anyGap |= gap;
            var side = sample.Celsius < min ? -1 : sample.Celsius > max ? 1 : 0;
            anyOutside |= side != 0;
            if (side == 0)
                outsideSince = null;
            else if (outsideSince is null || side != previousSide || gap)
                outsideSince = sample.ObservedAt;
            else if (sample.ObservedAt - outsideSince.Value >= ExcursionDuration)
                return new(AssessmentStatus.Excursion, outsideSince, sample.ObservedAt);

            previous = sample.ObservedAt;
            previousSide = side;
        }

        return new(count < 2 || anyGap || anyOutside ? AssessmentStatus.InsufficientEvidence : AssessmentStatus.WithinRange);
    }
}
