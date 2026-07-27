using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadRecovery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledActionTelemetryContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "correlation_id",
                table: "scheduled_actions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "trace_parent",
                table: "scheduled_actions",
                type: "character varying(55)",
                maxLength: 55,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "trace_state",
                table: "scheduled_actions",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "correlation_id",
                table: "scheduled_actions");

            migrationBuilder.DropColumn(
                name: "trace_parent",
                table: "scheduled_actions");

            migrationBuilder.DropColumn(
                name: "trace_state",
                table: "scheduled_actions");
        }
    }
}
