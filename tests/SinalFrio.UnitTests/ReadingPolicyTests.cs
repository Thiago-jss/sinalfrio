using SinalFrio.Core;
using Xunit;

namespace SinalFrio.UnitTests;

public sealed class ReadingPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AcceptsBoundaryTimeAndNormalizesOffsetAndPrecision()
    {
        Assert.True(ReadingPolicy.TryNormalize(Guid.NewGuid(), Now.AddMinutes(5).ToOffset(TimeSpan.FromHours(-3)), -2.345m, Now, out var reading));
        Assert.Equal(TimeSpan.Zero, reading.ObservedAt.Offset);
        Assert.Equal(Now.AddMinutes(5), reading.ObservedAt);
        Assert.Equal(-2.35m, reading.Celsius);
    }

    [Fact]
    public void RejectsFutureReadingBeforeTruncation() =>
        Assert.False(ReadingPolicy.TryNormalize(Guid.NewGuid(), Now.AddMinutes(5).AddTicks(1), 0, Now, out _));

    [Fact]
    public void RejectsImplausibleTemperatureBeforeRounding() =>
        Assert.False(ReadingPolicy.TryNormalize(Guid.NewGuid(), Now, 80.001m, Now, out _));

    [Fact]
    public void RejectsEmptyIdentity() =>
        Assert.False(ReadingPolicy.TryNormalize(Guid.Empty, Now, 0, Now, out _));
}
