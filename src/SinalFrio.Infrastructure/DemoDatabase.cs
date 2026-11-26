using Microsoft.EntityFrameworkCore;
using SinalFrio.Core;

namespace SinalFrio.Infrastructure;

public static class DemoDatabase
{
    public static async Task MigrateAndSeedAsync(SinalFrioDbContext database, CancellationToken ct)
    {
        await database.Database.MigrateAsync(ct);
        await using var transaction = await database.Database.BeginTransactionAsync(ct);
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.sf_organizations ("Id", "Name") VALUES ({DemoScope.OrganizationId}, 'SinalFrio Demo')
            ON CONFLICT ("Id") DO NOTHING
            """, ct);
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.sf_shipments
                ("Id", "OrganizationId", "Code", "Route", "TemperatureMin", "TemperatureMax", "ReadingVersion", "EvaluatedVersion", "Assessment")
            VALUES ({DemoScope.PrimaryShipmentId}, {DemoScope.OrganizationId}, 'SF-001', 'São Paulo → Campinas', 2, 8, 0, 0, 0),
                   ({DemoScope.SecondaryShipmentId}, {DemoScope.OrganizationId}, 'SF-002', 'Curitiba → Joinville', 2, 8, 0, 0, 0)
            ON CONFLICT ("Id") DO NOTHING
            """, ct);
        await transaction.CommitAsync(ct);
    }
}
