using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SinalFrio.Infrastructure;

namespace SinalFrio.Api;

internal sealed class DatabaseHealthCheck(SinalFrioDbContext database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!await database.Database.CanConnectAsync(cancellationToken)) return HealthCheckResult.Unhealthy();
        var pending = await database.Database.GetPendingMigrationsAsync(cancellationToken);
        return pending.Any() ? HealthCheckResult.Unhealthy("Database migrations are pending.") : HealthCheckResult.Healthy();
    }
}
