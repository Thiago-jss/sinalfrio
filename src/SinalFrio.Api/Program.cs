using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using SinalFrio.Api;
using SinalFrio.Infrastructure;

var migrate = args is ["--migrate"];
var builder = WebApplication.CreateBuilder(migrate ? [] : args);
var connection = builder.Configuration.GetConnectionString("Database");
builder.Services.AddSinalFrioPersistence(!string.IsNullOrWhiteSpace(connection) ? connection
    : throw new InvalidOperationException("ConnectionStrings__Database is required."));
builder.Services.AddSingleton(DemoCredentials.FromEnvironment());
builder.Services.AddAuthentication(DemoAuthentication.Scheme)
    .AddScheme<AuthenticationSchemeOptions, DemoAuthentication>(DemoAuthentication.Scheme, _ => { });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(DemoAuthentication.SensorRole, policy => policy.RequireRole(DemoAuthentication.SensorRole))
    .AddPolicy(DemoAuthentication.OperatorRole, policy => policy.RequireRole(DemoAuthentication.OperatorRole));
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});
builder.Services.Configure<ApiBehaviorOptions>(options => options.InvalidModelStateResponseFactory = _ =>
    new BadRequestObjectResult(new ProblemDetails { Status = 400, Title = "Invalid request body or route values." }));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"], timeout: TimeSpan.FromSeconds(3));

await using var app = builder.Build();
if (migrate)
{
    await using var scope = app.Services.CreateAsyncScope();
    await DemoDatabase.MigrateAndSeedAsync(scope.ServiceProvider.GetRequiredService<SinalFrioDbContext>(), app.Lifetime.ApplicationStopping);
    return;
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapOpenApi(); // This application is the demo; no credentials are included in its schema.
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") });
await app.RunAsync();
