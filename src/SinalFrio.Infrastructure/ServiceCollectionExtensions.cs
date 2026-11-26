using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SinalFrio.Core;

namespace SinalFrio.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSinalFrioPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<SinalFrioDbContext>(options => options.UseNpgsql(connectionString));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IShipmentService, ShipmentService>();
        services.AddScoped<IShipmentEvaluator, ShipmentEvaluator>();
        return services;
    }
}
