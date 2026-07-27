using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861

namespace LeadRecovery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQualificationBookingWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "qualification_answers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_qualification_answers", x => x.id);
                    table.UniqueConstraint("ak_qualification_answers_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_qualification_answers_outcome", "outcome in ('Accepted', 'Unknown', 'Ambiguous')");
                    table.ForeignKey(
                        name: "fk_qualification_answers_leads_tenant_id_lead_id",
                        columns: x => new { x.tenant_id, x.lead_id },
                        principalTable: "leads",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_qualification_answers_messages_tenant_id_source_message_id",
                        columns: x => new { x.tenant_id, x.source_message_id },
                        principalTable: "messages",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_qualification_answers_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    booking_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    follow_up_policy_json = table.Column<string>(type: "jsonb", maxLength: 16384, nullable: false),
                    business_hours_policy_json = table.Column<string>(type: "jsonb", maxLength: 16384, nullable: false),
                    qualification_policy_json = table.Column<string>(type: "jsonb", maxLength: 16384, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_definitions", x => x.id);
                    table.UniqueConstraint("ak_workflow_definitions_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_workflow_definitions_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_qualification_answers_tenant_lead_question",
                table: "qualification_answers",
                columns: new[] { "tenant_id", "lead_id", "question_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_qualification_answers_tenant_source_message",
                table: "qualification_answers",
                columns: new[] { "tenant_id", "source_message_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_workflow_definitions_tenant_active",
                table: "workflow_definitions",
                column: "tenant_id",
                unique: true,
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "ux_workflow_definitions_tenant_version",
                table: "workflow_definitions",
                columns: new[] { "tenant_id", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "qualification_answers");

            migrationBuilder.DropTable(
                name: "workflow_definitions");
        }
    }
}
