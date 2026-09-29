using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceOps.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkOrderCreation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "WorkOrderNumbers",
                startValue: 10001L);

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Technicians",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Technicians", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Locations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Locations_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValueSql: "'WO-' || nextval('\"WorkOrderNumbers\"')"),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    ServiceType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SlaPolicyVersion = table.Column<int>(type: "integer", nullable: false),
                    SlaDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    SlaAtRiskAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SlaDeadlineAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    SlaRevision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrders", x => x.Id);
                    table.CheckConstraint("CK_WorkOrder_Priority", "\"Priority\" IN ('Critical', 'High', 'Normal', 'Low')");
                    table.CheckConstraint("CK_WorkOrder_Revisions", "\"Revision\" > 0 AND \"SlaRevision\" > 0 AND \"SlaPolicyVersion\" > 0");
                    table.CheckConstraint("CK_WorkOrder_ServiceType", "\"ServiceType\" IN ('HVAC', 'Electrical', 'Plumbing', 'Equipment', 'GeneralMaintenance')");
                    table.CheckConstraint("CK_WorkOrder_Sla", "\"SlaDurationMinutes\" > 0 AND \"CreatedAt\" < \"SlaAtRiskAt\" AND \"SlaAtRiskAt\" < \"SlaDeadlineAt\"");
                    table.CheckConstraint("CK_WorkOrder_Status", "\"Status\" = 'New'");
                    table.CheckConstraint("CK_WorkOrder_Text", "length(btrim(\"Title\")) > 0 AND length(btrim(\"Description\")) > 0");
                    table.ForeignKey(
                        name: "FK_WorkOrders_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrders_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    EffectiveAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderActivities", x => x.Id);
                    table.CheckConstraint("CK_Activity_EventType", "\"EventType\" = 'Created'");
                    table.ForeignKey(
                        name: "FK_WorkOrderActivities_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderActivities_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Code",
                table: "Customers",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Locations_CustomerId_Code",
                table: "Locations",
                columns: new[] { "CustomerId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderActivities_ActorUserId",
                table: "WorkOrderActivities",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderActivities_WorkOrderId",
                table: "WorkOrderActivities",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_CreatedByUserId",
                table: "WorkOrders",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_LocationId",
                table: "WorkOrders",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_Number",
                table: "WorkOrders",
                column: "Number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Technicians");

            migrationBuilder.DropTable(
                name: "WorkOrderActivities");

            migrationBuilder.DropTable(
                name: "WorkOrders");

            migrationBuilder.DropTable(
                name: "Locations");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropSequence(
                name: "WorkOrderNumbers");
        }
    }
}
