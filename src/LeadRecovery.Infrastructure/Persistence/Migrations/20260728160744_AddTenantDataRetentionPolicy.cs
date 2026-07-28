using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadRecovery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantDataRetentionPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "data_retention_days",
                table: "tenants",
                type: "integer",
                nullable: false,
                defaultValue: 365);

            migrationBuilder.AddColumn<bool>(
                name: "data_retention_enabled",
                table: "tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenants_data_retention_days",
                table: "tenants",
                sql: "data_retention_days between 30 and 3650");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tenants_data_retention_days",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "data_retention_days",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "data_retention_enabled",
                table: "tenants");
        }
    }
}
