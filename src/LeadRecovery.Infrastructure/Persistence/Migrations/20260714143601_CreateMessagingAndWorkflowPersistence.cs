using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadRecovery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateMessagingAndWorkflowPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "external_event_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    external_event_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    processing_result = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_event_receipts", x => x.id);
                    table.ForeignKey(
                        name: "fk_external_event_receipts_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    primary_phone_e164 = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    urgency = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    service_category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    automation_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    last_customer_activity_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_business_activity_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    booked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    close_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leads", x => x.id);
                    table.UniqueConstraint("ak_leads_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_leads_automation_state", "automation_state in ('Active', 'PausedByUser', 'PausedBySystem', 'Completed', 'SuppressedOptOut', 'SuppressedPolicy')");
                    table.CheckConstraint("ck_leads_source", "source in ('MissedCall', 'InboundSms', 'WebForm', 'Manual', 'Import')");
                    table.CheckConstraint("ck_leads_status", "status in ('New', 'Contacting', 'AwaitingCustomer', 'Qualified', 'BookingOffered', 'NeedsHuman', 'Booked', 'Closed', 'ClosedWon')");
                    table.CheckConstraint("ck_leads_urgency", "urgency in ('Unknown', 'Low', 'Normal', 'High', 'CriticalReview')");
                    table.ForeignKey(
                        name: "fk_leads_customers_tenant_id_customer_id",
                        columns: x => new { x.tenant_id, x.customer_id },
                        principalTable: "customers",
                        principalColumns: ["tenant_id", "id"],
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leads_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "conversations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversations", x => x.id);
                    table.UniqueConstraint("ak_conversations_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_conversations_channel", "channel in ('Sms')");
                    table.CheckConstraint("ck_conversations_status", "status in ('Open', 'Closed')");
                    table.ForeignKey(
                        name: "fk_conversations_leads_tenant_id_lead_id",
                        columns: x => new { x.tenant_id, x.lead_id },
                        principalTable: "leads",
                        principalColumns: ["tenant_id", "id"],
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_conversations_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "scheduled_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    scheduled_for_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheduled_actions", x => x.id);
                    table.UniqueConstraint("ak_scheduled_actions_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_scheduled_actions_attempt_count", "attempt_count >= 0");
                    table.CheckConstraint("ck_scheduled_actions_status", "status in ('Pending', 'Running', 'Completed', 'Cancelled', 'Failed')");
                    table.ForeignKey(
                        name: "fk_scheduled_actions_leads_tenant_id_lead_id",
                        columns: x => new { x.tenant_id, x.lead_id },
                        principalTable: "leads",
                        principalColumns: ["tenant_id", "id"],
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_scheduled_actions_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    provider_message_sid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    client_idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(1600)", maxLength: 1600, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    failure_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    failure_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sent_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_messages", x => x.id);
                    table.UniqueConstraint("ak_messages_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_messages_direction", "direction in ('Inbound', 'Outbound')");
                    table.CheckConstraint("ck_messages_kind", "kind in ('Automated', 'Manual', 'System')");
                    table.CheckConstraint("ck_messages_status", "status in ('Queued', 'Sent', 'Delivered', 'Failed', 'Received', 'Suppressed')");
                    table.ForeignKey(
                        name: "fk_messages_conversations_tenant_id_conversation_id",
                        columns: x => new { x.tenant_id, x.conversation_id },
                        principalTable: "conversations",
                        principalColumns: ["tenant_id", "id"],
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_messages_leads_tenant_id_lead_id",
                        columns: x => new { x.tenant_id, x.lead_id },
                        principalTable: "leads",
                        principalColumns: ["tenant_id", "id"],
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_messages_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_tenant_lead_created",
                table: "conversations",
                columns: ["tenant_id", "lead_id", "created_at_utc"],
                descending: [false, false, true]);

            migrationBuilder.CreateIndex(
                name: "ix_external_event_receipts_received_at",
                table: "external_event_receipts",
                column: "received_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_external_event_receipts_tenant_id",
                table: "external_event_receipts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_external_event_receipts_provider_event_identity",
                table: "external_event_receipts",
                columns: ["provider", "event_type", "external_event_id"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leads_tenant_assigned_status",
                table: "leads",
                columns: ["tenant_id", "assigned_user_id", "status"]);

            migrationBuilder.CreateIndex(
                name: "IX_leads_tenant_id_customer_id",
                table: "leads",
                columns: ["tenant_id", "customer_id"]);

            migrationBuilder.CreateIndex(
                name: "ix_leads_tenant_phone_created",
                table: "leads",
                columns: ["tenant_id", "primary_phone_e164", "created_at_utc"],
                descending: [false, false, true]);

            migrationBuilder.CreateIndex(
                name: "ix_leads_tenant_status_created",
                table: "leads",
                columns: ["tenant_id", "status", "created_at_utc"],
                descending: [false, false, true]);

            migrationBuilder.CreateIndex(
                name: "ix_leads_tenant_urgency_status",
                table: "leads",
                columns: ["tenant_id", "urgency", "status"]);

            migrationBuilder.CreateIndex(
                name: "ix_messages_tenant_conversation_created",
                table: "messages",
                columns: ["tenant_id", "conversation_id", "created_at_utc"]);

            migrationBuilder.CreateIndex(
                name: "IX_messages_tenant_id_lead_id",
                table: "messages",
                columns: ["tenant_id", "lead_id"]);

            migrationBuilder.CreateIndex(
                name: "ux_messages_provider_sid",
                table: "messages",
                columns: ["provider", "provider_message_sid"],
                unique: true,
                filter: "provider_message_sid is not null");

            migrationBuilder.CreateIndex(
                name: "ux_messages_tenant_id_client_idempotency_key",
                table: "messages",
                columns: ["tenant_id", "client_idempotency_key"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_actions_due",
                table: "scheduled_actions",
                columns: ["status", "scheduled_for_utc"]);

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_actions_tenant_lead_status",
                table: "scheduled_actions",
                columns: ["tenant_id", "lead_id", "status"]);

            migrationBuilder.CreateIndex(
                name: "ux_scheduled_actions_tenant_id_idempotency_key",
                table: "scheduled_actions",
                columns: ["tenant_id", "idempotency_key"],
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "external_event_receipts");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "scheduled_actions");

            migrationBuilder.DropTable(
                name: "conversations");

            migrationBuilder.DropTable(
                name: "leads");
        }
    }
}
