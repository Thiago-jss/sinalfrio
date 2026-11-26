using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using RabbitMQ.Client;
using SinalFrio.Infrastructure;
using SinalFrio.Worker;

var builder = Host.CreateApplicationBuilder(args);
string Required(string key) => builder.Configuration[key] is { } value && !string.IsNullOrWhiteSpace(value)
    ? value : throw new InvalidOperationException($"Configuration key '{key}' is required.");

var database = Required("ConnectionStrings:Database");
try
{
    var settings = new NpgsqlConnectionStringBuilder(database);
    if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.Database))
        throw new ArgumentException();
}
catch (ArgumentException)
{
    throw new InvalidOperationException("ConnectionStrings:Database must be a valid PostgreSQL connection string with Host and Database.");
}

var port = 5672;
if (builder.Configuration["Messaging:Port"] is { } rawPort &&
    (!int.TryParse(rawPort, out port) || port is < 1 or > 65535))
    throw new InvalidOperationException("Messaging:Port must be a TCP port from 1 to 65535.");

var host = Required("Messaging:Host");
if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
    throw new InvalidOperationException("Messaging:Host must be a hostname or IP address.");

builder.Services.AddSingleton(new ConnectionFactory
{
    HostName = host,
    Port = port,
    UserName = Required("Messaging:Username"),
    Password = Required("Messaging:Password"),
    AutomaticRecoveryEnabled = false, // Each service restores its entire session explicitly.
    RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
    ContinuationTimeout = TimeSpan.FromSeconds(5),
    RequestedHeartbeat = TimeSpan.FromSeconds(10),
    ConsumerDispatchConcurrency = 1
});
builder.Services.AddSinalFrioPersistence(database);
builder.Services.AddSingleton(_ => new OutboxStore(NpgsqlDataSource.Create(database)));
builder.Services.AddHostedService<OutboxRelay>();
builder.Services.AddHostedService<TelemetryConsumer>();

using var app = builder.Build();
await app.RunAsync();