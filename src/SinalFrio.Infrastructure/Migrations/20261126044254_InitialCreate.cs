using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SinalFrio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "public");

            migrationBuilder.CreateTable(
                name: "sf_inbox",
                schema: "public",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sf_inbox", x => x.message_id);
                });

            migrationBuilder.CreateTable(
                name: "sf_organizations",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sf_organizations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sf_outbox",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sf_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sf_shipments",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Route = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TemperatureMin = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: false),
                    TemperatureMax = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: false),
                    ReadingVersion = table.Column<long>(type: "bigint", nullable: false),
                    EvaluatedVersion = table.Column<long>(type: "bigint", nullable: false),
                    Assessment = table.Column<int>(type: "integer", nullable: false),
                    ExcursionStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExcursionDetectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sf_shipments", x => x.Id);
                    table.CheckConstraint("ck_shipment_versions", "\"ReadingVersion\" BETWEEN 0 AND 2000 AND \"EvaluatedVersion\" BETWEEN 0 AND \"ReadingVersion\"");
                    table.ForeignKey(
                        name: "FK_sf_shipments_sf_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "public",
                        principalTable: "sf_organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sf_readings",
                schema: "public",
                columns: table => new
                {
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Celsius = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: false),
                    SubmittedCelsius = table.Column<decimal>(type: "numeric", nullable: false),
                    SubmittedUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    SensorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sf_readings", x => new { x.ShipmentId, x.Id });
                    table.ForeignKey(
                        name: "FK_sf_readings_sf_shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalSchema: "public",
                        principalTable: "sf_shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sf_reviews",
                schema: "public",
                columns: table => new
                {
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluatedVersion = table.Column<long>(type: "bigint", nullable: false),
                    Decision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OperatorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sf_reviews", x => new { x.ShipmentId, x.EvaluatedVersion });
                    table.ForeignKey(
                        name: "FK_sf_reviews_sf_shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalSchema: "public",
                        principalTable: "sf_shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sf_outbox_occurred_at_id",
                schema: "public",
                table: "sf_outbox",
                columns: new[] { "occurred_at", "id" },
                filter: "published_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sf_readings_ShipmentId_ObservedAt",
                schema: "public",
                table: "sf_readings",
                columns: new[] { "ShipmentId", "ObservedAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sf_shipments_OrganizationId_Code",
                schema: "public",
                table: "sf_shipments",
                columns: new[] { "OrganizationId", "Code" },
                unique: true);

            migrationBuilder.Sql("""
                GRANT SELECT ON public.sf_shipments, public.sf_readings,
                    public.sf_reviews, public.sf_outbox, public.sf_inbox TO sinalfrio_runtime;
                GRANT INSERT ON public.sf_readings, public.sf_reviews,
                    public.sf_outbox, public.sf_inbox TO sinalfrio_runtime;
                GRANT UPDATE ("ReadingVersion", "EvaluatedVersion", "Assessment",
                    "ExcursionStartedAt", "ExcursionDetectedAt") ON public.sf_shipments TO sinalfrio_runtime;
                GRANT UPDATE (published_at) ON public.sf_outbox TO sinalfrio_runtime;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sf_inbox",
                schema: "public");

            migrationBuilder.DropTable(
                name: "sf_outbox",
                schema: "public");

            migrationBuilder.DropTable(
                name: "sf_readings",
                schema: "public");

            migrationBuilder.DropTable(
                name: "sf_reviews",
                schema: "public");

            migrationBuilder.DropTable(
                name: "sf_shipments",
                schema: "public");

            migrationBuilder.DropTable(
                name: "sf_organizations",
                schema: "public");
        }
    }
}
