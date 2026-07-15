using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadRecovery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTwilioCallIngestion : Migration
    {
        private static readonly string[] ProviderAndNumberSidColumns =
            ["provider", "provider_number_sid"];
        private static readonly string[] ProviderAndPhoneColumns =
            ["provider", "phone_number_e164"];
        private static readonly string[] TenantAndPhoneColumns =
            ["tenant_id", "phone_number_e164"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_phone_numbers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    phone_number_e164 = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    provider_number_sid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    inbound_sms_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    missed_call_recovery_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    recoverable_call_statuses = table.Column<string[]>(type: "text[]", nullable: false),
                    initial_delay_seconds = table.Column<int>(type: "integer", nullable: false),
                    recovery_cooldown_seconds = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_phone_numbers", x => x.id);
                    table.UniqueConstraint("ak_tenant_phone_numbers_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_tenant_phone_numbers_initial_delay_seconds", "initial_delay_seconds between 0 and 3600");
                    table.CheckConstraint("ck_tenant_phone_numbers_recoverable_statuses", "cardinality(recoverable_call_statuses) > 0");
                    table.CheckConstraint("ck_tenant_phone_numbers_recovery_cooldown_seconds", "recovery_cooldown_seconds between 1 and 86400");
                    table.ForeignKey(
                        name: "fk_tenant_phone_numbers_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_tenant_phone_numbers_provider_number_sid",
                table: "tenant_phone_numbers",
                columns: ProviderAndNumberSidColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_tenant_phone_numbers_provider_phone_number",
                table: "tenant_phone_numbers",
                columns: ProviderAndPhoneColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_tenant_phone_numbers_tenant_phone_number",
                table: "tenant_phone_numbers",
                columns: TenantAndPhoneColumns,
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_phone_numbers");
        }
    }
}
