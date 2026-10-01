using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceOps.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkOrderWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkOrder_Status",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrderActivities_WorkOrderId",
                table: "WorkOrderActivities");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Activity_EventType",
                table: "WorkOrderActivities");

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "WorkOrders",
                type: "character varying(5000)",
                maxLength: 5000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                table: "WorkOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                table: "WorkOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HoldReason",
                table: "WorkOrders",
                type: "character varying(5000)",
                maxLength: 5000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolutionSummary",
                table: "WorkOrders",
                type: "character varying(5000)",
                maxLength: 5000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TechnicianId",
                table: "WorkOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "WorkOrders",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.Sql("UPDATE \"WorkOrders\" SET \"UpdatedAt\" = \"CreatedAt\"");



            migrationBuilder.AddColumn<string>(
                name: "Changes",
                table: "WorkOrderActivities",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_TechnicianId",
                table: "WorkOrders",
                column: "TechnicianId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkOrder_Assignment",
                table: "WorkOrders",
                sql: "(\"Status\" <> 'New' OR \"TechnicianId\" IS NULL) AND (\"Status\" NOT IN ('Assigned', 'InProgress', 'OnHold', 'Completed') OR \"TechnicianId\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkOrder_Hold",
                table: "WorkOrders",
                sql: "(\"Status\" = 'OnHold' AND length(btrim(coalesce(\"HoldReason\", ''))) > 0) OR (\"Status\" <> 'OnHold' AND \"HoldReason\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkOrder_Status",
                table: "WorkOrders",
                sql: "\"Status\" IN ('New', 'Assigned', 'InProgress', 'OnHold', 'Completed', 'Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkOrder_Terminal",
                table: "WorkOrders",
                sql: "((\"Status\" = 'Completed' AND \"CompletedAt\" IS NOT NULL AND \"CompletedAt\" >= \"CreatedAt\" AND length(btrim(coalesce(\"ResolutionSummary\", ''))) > 0) OR (\"Status\" <> 'Completed' AND \"CompletedAt\" IS NULL AND \"ResolutionSummary\" IS NULL)) AND ((\"Status\" = 'Cancelled' AND \"CancelledAt\" IS NOT NULL AND \"CancelledAt\" >= \"CreatedAt\" AND length(btrim(coalesce(\"CancellationReason\", ''))) > 0) OR (\"Status\" <> 'Cancelled' AND \"CancelledAt\" IS NULL AND \"CancellationReason\" IS NULL))");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderActivities_WorkOrderId_EffectiveAt_Id",
                table: "WorkOrderActivities",
                columns: new[] { "WorkOrderId", "EffectiveAt", "Id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Activity_EventType",
                table: "WorkOrderActivities",
                sql: "\"EventType\" IN ('Created', 'Assigned', 'Reassigned', 'Unassigned', 'DetailsCorrected', 'Started', 'PlacedOnHold', 'Resumed', 'Completed', 'Cancelled')");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrders_Technicians_TechnicianId",
                table: "WorkOrders",
                column: "TechnicianId",
                principalTable: "Technicians",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrders_Technicians_TechnicianId",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_TechnicianId",
                table: "WorkOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkOrder_Assignment",
                table: "WorkOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkOrder_Hold",
                table: "WorkOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkOrder_Status",
                table: "WorkOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkOrder_Terminal",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrderActivities_WorkOrderId_EffectiveAt_Id",
                table: "WorkOrderActivities");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Activity_EventType",
                table: "WorkOrderActivities");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "HoldReason",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "ResolutionSummary",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "TechnicianId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "Changes",
                table: "WorkOrderActivities");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkOrder_Status",
                table: "WorkOrders",
                sql: "\"Status\" = 'New'");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderActivities_WorkOrderId",
                table: "WorkOrderActivities",
                column: "WorkOrderId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Activity_EventType",
                table: "WorkOrderActivities",
                sql: "\"EventType\" = 'Created'");
        }
    }
}
