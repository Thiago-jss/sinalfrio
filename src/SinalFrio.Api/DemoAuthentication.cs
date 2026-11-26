using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SinalFrio.Core;

namespace SinalFrio.Api;

internal sealed class DemoCredentials
{
    private readonly byte[] _sensor;
    private readonly byte[] _operator;

    private DemoCredentials(byte[] sensor, byte[] @operator) => (_sensor, _operator) = (sensor, @operator);

    public static DemoCredentials FromEnvironment()
    {
        var sensor = Environment.GetEnvironmentVariable("Demo__SensorToken");
        var @operator = Environment.GetEnvironmentVariable("Demo__OperatorToken");
        if (!IsValid(sensor) || !IsValid(@operator)) throw InvalidConfiguration();
        var sensorHash = Hash(sensor!);
        var operatorHash = Hash(@operator!);
        if (CryptographicOperations.FixedTimeEquals(sensorHash, operatorHash)) throw InvalidConfiguration();
        return new(sensorHash, operatorHash);
    }

    public string? ResolveRole(string token)
    {
        var hash = Hash(token);
        var sensor = CryptographicOperations.FixedTimeEquals(hash, _sensor);
        var @operator = CryptographicOperations.FixedTimeEquals(hash, _operator);
        return sensor ? DemoAuthentication.SensorRole : @operator ? DemoAuthentication.OperatorRole : null;
    }

    private static bool IsValid(string? token) => token is { Length: >= 32 } && token.All(c => c is >= '!' and <= '~' && c != ',');
    private static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
    private static InvalidOperationException InvalidConfiguration() =>
        new("Demo__SensorToken and Demo__OperatorToken must be distinct bearer tokens of at least 32 characters.");
}

internal sealed class DemoAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, DemoCredentials credentials) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public new const string Scheme = "DemoBearer";
    public const string SensorRole = "Sensor";
    public const string OperatorRole = "Operator";
    public const string OrganizationClaim = "organization_id";
    public const string ShipmentClaim = "shipment_id";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization;
        if (header.Count == 0) return Task.FromResult(AuthenticateResult.NoResult());
        if (header.Count != 1 || !AuthenticationHeaderValue.TryParse(header[0], out var value) ||
            !string.Equals(value.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) || value.Parameter is null ||
            credentials.ResolveRole(value.Parameter) is not { } role)
            return Task.FromResult(AuthenticateResult.Fail("Invalid bearer credentials."));

        var isSensor = role == SensorRole;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, isSensor ? DemoScope.SensorId : DemoScope.OperatorId),
            new(ClaimTypes.Role, role),
            new(OrganizationClaim, DemoScope.OrganizationId.ToString())
        };
        if (isSensor) claims.Add(new(ShipmentClaim, DemoScope.PrimaryShipmentId.ToString()));
        return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme)), Scheme)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        return base.HandleChallengeAsync(properties);
    }
}
