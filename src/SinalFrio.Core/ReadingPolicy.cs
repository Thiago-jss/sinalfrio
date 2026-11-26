namespace SinalFrio.Core;

public static class ReadingPolicy
{
    public static bool TryNormalize(Guid readingId, DateTimeOffset observedAt, decimal celsius, DateTimeOffset now, out ThermalSample sample)
    {
        sample = default;
        if (readingId == Guid.Empty || observedAt == default || observedAt > now.AddMinutes(5) || celsius is < -80 or > 80)
            return false;

        // PostgreSQL timestamptz stores whole microseconds.
        var utcTicks = observedAt.UtcTicks;
        sample = new(new(utcTicks - utcTicks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero),
            decimal.Round(celsius, 2, MidpointRounding.AwayFromZero));
        return true;
    }
}
