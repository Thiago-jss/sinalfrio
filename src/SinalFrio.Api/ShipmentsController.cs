using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SinalFrio.Core;

namespace SinalFrio.Api;

[ApiController]
[Route("api/v1/shipments")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ShipmentsController(IShipmentService shipments) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = DemoAuthentication.OperatorRole)]
    public async Task<ActionResult<ShipmentSummary[]>> List(CancellationToken ct) =>
        Ok(await shipments.ListAsync(OrganizationId, ct));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = DemoAuthentication.OperatorRole)]
    public async Task<ActionResult<ShipmentDetail>> Get(Guid id, CancellationToken ct) =>
        await shipments.GetAsync(OrganizationId, id, ct) is { } detail ? Ok(detail) : NotFound();

    [HttpPost("{id:guid}/readings")]
    [Authorize(Policy = DemoAuthentication.SensorRole)]
    [RequestSizeLimit(4096)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Ingest(Guid id, IngestRequest input, CancellationToken ct)
    {
        var actor = new SensorActor(ActorId, OrganizationId, Guid.Parse(User.FindFirstValue(DemoAuthentication.ShipmentClaim)!));
        var result = await shipments.IngestAsync(actor, id, input.Id, input.ObservedAt, input.Celsius, ct);
        return result.Status == WriteStatus.Success ? Accepted() : Error(result);
    }

    [HttpPost("{id:guid}/reviews")]
    [Authorize(Policy = DemoAuthentication.OperatorRole)]
    [RequestSizeLimit(16384)]
    public async Task<ActionResult<ShipmentDetail>> Review(Guid id, ReviewRequest input, CancellationToken ct)
    {
        var result = await shipments.ReviewAsync(new(ActorId, OrganizationId), id, input.ExpectedVersion, input.Decision, input.Note, ct);
        return result.Status == WriteStatus.Success ? Ok(result.Detail) : Error(result);
    }

    private Guid OrganizationId => Guid.Parse(User.FindFirstValue(DemoAuthentication.OrganizationClaim)!);
    private string ActorId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private ActionResult Error(WriteResult result) => result.Status switch
    {
        WriteStatus.Forbidden => Forbid(),
        WriteStatus.NotFound => NotFound(),
        WriteStatus.Invalid => Problem(statusCode: 400, title: result.Reason),
        WriteStatus.Conflict => Problem(statusCode: 409, title: result.Reason),
        _ => throw new InvalidOperationException("Unexpected shipment outcome.")
    };
}

public sealed class IngestRequest
{
    public required Guid Id { get; init; }
    [JsonConverter(typeof(ExplicitOffsetConverter))]
    public required DateTimeOffset ObservedAt { get; init; }
    public required decimal Celsius { get; init; }
}

public sealed class ReviewRequest
{
    public required long ExpectedVersion { get; init; }
    [Required]
    public required string Decision { get; init; }
    [Required, StringLength(1000)]
    public required string Note { get; init; }
}

internal sealed class ExplicitOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && reader.GetString() is { } value &&
            (value.EndsWith('Z') || (value.Length > 6 && value[^6] is '+' or '-' && value[^3] == ':')) &&
            reader.TryGetDateTimeOffset(out var time)) return time;
        throw new JsonException("An ISO 8601 timestamp with explicit UTC offset is required.");
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
