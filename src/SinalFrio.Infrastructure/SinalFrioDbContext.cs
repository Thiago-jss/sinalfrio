using Microsoft.EntityFrameworkCore;
using SinalFrio.Core;

namespace SinalFrio.Infrastructure;

public sealed class SinalFrioDbContext(DbContextOptions<SinalFrioDbContext> options) : DbContext(options)
{
    internal DbSet<Shipment> Shipments => Set<Shipment>();
    internal DbSet<Reading> Readings => Set<Reading>();
    internal DbSet<Review> Reviews => Set<Review>();
    internal DbSet<OutboxEntry> Outbox => Set<OutboxEntry>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("public");
        model.Entity<Organization>(b =>
        {
            b.ToTable("sf_organizations");
            b.Property(e => e.Name).HasMaxLength(100);
        });
        model.Entity<Shipment>(b =>
        {
            b.ToTable("sf_shipments", t => t.HasCheckConstraint("ck_shipment_versions", "\"ReadingVersion\" BETWEEN 0 AND 2000 AND \"EvaluatedVersion\" BETWEEN 0 AND \"ReadingVersion\""));
            b.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId);
            b.HasIndex(e => new { e.OrganizationId, e.Code }).IsUnique();
            b.Property(e => e.Code).HasMaxLength(32);
            b.Property(e => e.Route).HasMaxLength(200);
            b.Property(e => e.TemperatureMin).HasPrecision(4, 2);
            b.Property(e => e.TemperatureMax).HasPrecision(4, 2);
        });
        model.Entity<Reading>(b =>
        {
            b.ToTable("sf_readings");
            b.HasKey(e => new { e.ShipmentId, e.Id });
            b.HasIndex(e => new { e.ShipmentId, e.ObservedAt }).IsUnique();
            b.HasOne<Shipment>().WithMany().HasForeignKey(e => e.ShipmentId);
            b.Property(e => e.Celsius).HasPrecision(4, 2);
            b.Property(e => e.SensorId).HasMaxLength(64);
        });
        model.Entity<Review>(b =>
        {
            b.ToTable("sf_reviews");
            b.HasKey(e => new { e.ShipmentId, e.EvaluatedVersion });
            b.HasOne<Shipment>().WithMany().HasForeignKey(e => e.ShipmentId);
            b.Property(e => e.Decision).HasMaxLength(32);
            b.Property(e => e.Note).HasMaxLength(1000);
            b.Property(e => e.OperatorId).HasMaxLength(64);
        });
        model.Entity<OutboxEntry>(b =>
        {
            b.ToTable("sf_outbox");
            b.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(e => e.Payload).HasColumnName("payload").HasColumnType("jsonb");
            b.Property(e => e.OccurredAt).HasColumnName("occurred_at");
            b.Property(e => e.PublishedAt).HasColumnName("published_at");
            b.HasIndex(e => new { e.OccurredAt, e.Id }).HasFilter("published_at IS NULL");
        });
        model.Entity<InboxEntry>(b =>
        {
            b.ToTable("sf_inbox");
            b.HasKey(e => e.MessageId);
            b.Property(e => e.MessageId).HasColumnName("message_id").ValueGeneratedNever();
        });
    }

    // Do not compose LINQ over this SQL; materialize the single locked row directly.
    internal async Task<Shipment?> LockShipmentAsync(Guid id, CancellationToken ct) =>
        (await Shipments.FromSql($"""SELECT * FROM public.sf_shipments WHERE "Id" = {id} FOR UPDATE""")
            .AsNoTracking().ToListAsync(ct)).SingleOrDefault();
}

internal sealed class Organization
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
}

internal sealed class Shipment
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public required string Code { get; init; }
    public required string Route { get; init; }
    public decimal TemperatureMin { get; init; }
    public decimal TemperatureMax { get; init; }
    public long ReadingVersion { get; set; }
    public long EvaluatedVersion { get; set; }
    public AssessmentStatus Assessment { get; set; }
    public DateTimeOffset? ExcursionStartedAt { get; set; }
    public DateTimeOffset? ExcursionDetectedAt { get; set; }
}

internal sealed class Reading
{
    public Guid ShipmentId { get; init; }
    public Guid Id { get; init; }
    public DateTimeOffset ObservedAt { get; init; }
    public DateTimeOffset ReceivedAt { get; init; }
    public decimal Celsius { get; init; }
    public decimal SubmittedCelsius { get; init; }
    public long SubmittedUtcTicks { get; init; }
    public required string SensorId { get; init; }
}

internal sealed class Review
{
    public Guid ShipmentId { get; init; }
    public long EvaluatedVersion { get; init; }
    public required string Decision { get; init; }
    public required string Note { get; init; }
    public DateTimeOffset ReviewedAt { get; init; }
    public required string OperatorId { get; init; }
}

internal sealed class OutboxEntry
{
    public Guid Id { get; init; }
    public required string Payload { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
}

internal sealed class InboxEntry
{
    public Guid MessageId { get; init; }
}
