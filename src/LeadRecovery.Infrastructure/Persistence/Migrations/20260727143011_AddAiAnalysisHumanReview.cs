using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861 // EF-generated migration arrays are constructed once per migration.

namespace LeadRecovery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiAnalysisHumanReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_analyses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schema_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    model_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    input_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    allowed_categories_json = table.Column<string>(type: "jsonb", nullable: false),
                    category_suggestion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    urgency_suggestion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    extracted_city = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    extracted_postal_code = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    extracted_preferred_callback_window = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    suggested_reply = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    confidence = table.Column<double>(type: "double precision", nullable: false),
                    requires_human_review = table.Column<bool>(type: "boolean", nullable: false),
                    reason_codes_json = table.Column<string>(type: "jsonb", nullable: false),
                    raw_structured_output_json = table.Column<string>(type: "jsonb", nullable: false),
                    review_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reviewed_category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    reviewed_urgency = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    reviewed_summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    reviewed_city = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reviewed_postal_code = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reviewed_preferred_callback_window = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reviewed_suggested_reply = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    correction_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_analyses", x => x.id);
                    table.UniqueConstraint("ak_ai_analyses_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_ai_analyses_confidence", "confidence >= 0 and confidence <= 1");
                    table.CheckConstraint("ck_ai_analyses_review_metadata", "(review_status = 'Pending' and reviewed_by_user_id is null and reviewed_at_utc is null)\nor\n(review_status <> 'Pending' and reviewed_by_user_id is not null and reviewed_at_utc is not null)");
                    table.CheckConstraint("ck_ai_analyses_review_status", "review_status in ('Pending', 'Accepted', 'Edited', 'Rejected')");
                    table.CheckConstraint("ck_ai_analyses_review_values", "(review_status in ('Accepted', 'Edited') and reviewed_category is not null and reviewed_urgency is not null and reviewed_summary is not null)\nor\n(review_status in ('Pending', 'Rejected') and reviewed_category is null and reviewed_urgency is null and reviewed_summary is null)");
                    table.CheckConstraint("ck_ai_analyses_reviewed_urgency", "reviewed_urgency is null or reviewed_urgency in ('Unknown', 'Low', 'Normal', 'High', 'CriticalReview')");
                    table.CheckConstraint("ck_ai_analyses_urgency_suggestion", "urgency_suggestion in ('Unknown', 'Low', 'Normal', 'High', 'CriticalReview')");
                    table.ForeignKey(
                        name: "fk_ai_analyses_leads_tenant_id_lead_id",
                        columns: x => new { x.tenant_id, x.lead_id },
                        principalTable: "leads",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ai_analyses_memberships_tenant_id_reviewed_by_user_id",
                        columns: x => new { x.tenant_id, x.reviewed_by_user_id },
                        principalTable: "tenant_memberships",
                        principalColumns: new[] { "tenant_id", "user_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ai_analyses_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_analyses_tenant_lead_review_created",
                table: "ai_analyses",
                columns: new[] { "tenant_id", "lead_id", "review_status", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_analyses_tenant_reviewer",
                table: "ai_analyses",
                columns: new[] { "tenant_id", "reviewed_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_ai_analyses_tenant_lead_schema_input_hash",
                table: "ai_analyses",
                columns: new[] { "tenant_id", "lead_id", "schema_version", "input_hash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_analyses");
        }
    }
}
