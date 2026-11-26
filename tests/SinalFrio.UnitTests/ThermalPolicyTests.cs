using SinalFrio.Core;
using Xunit;

namespace SinalFrio.UnitTests;

public class ThermalPolicyTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(9, 8)]
    [InlineData(9, 1)]
    [InlineData(8, 1)]
    public void ShortOrMixedDeviationsAreNotLabeledWithinRange(int first, int second)
    {
        var result = ThermalPolicy.Assess([new(Start, first), new(Start.AddMinutes(5), second)], 2, 8);
        Assert.Equal(AssessmentStatus.InsufficientEvidence, result.Status);
    }

    [Fact]
    public void TwoInRangeSamplesWithNoGapReportWithinRange()
    {
        var result = ThermalPolicy.Assess([new(Start, 2), new(Start.AddMinutes(5), 8)], 2, 8);
        Assert.Equal(AssessmentStatus.WithinRange, result.Status);
    }

    [Fact]
    public void ExactlyTenMinutesOutsideOnSameSideConfirmsExcursion()
    {
        var result = ThermalPolicy.Assess([new(Start.AddMinutes(10), 9), new(Start, 9), new(Start.AddMinutes(5), 9)], 2, 8);
        Assert.Equal(AssessmentStatus.Excursion, result.Status);
        Assert.Equal(Start, result.StartedAt);
        Assert.Equal(Start.AddMinutes(10), result.DetectedAt);
    }
}
