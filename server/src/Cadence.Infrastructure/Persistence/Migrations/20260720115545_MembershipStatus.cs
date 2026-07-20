using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MembershipStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 'active', not EF's generated empty string: existing memberships are active, and an
            // empty string would fail the check constraint added immediately below — on the very
            // same migration that creates it.
            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "organization_member",
                type: "text",
                nullable: false,
                defaultValue: "active");

            migrationBuilder.AddCheckConstraint(
                name: "ck_organization_member_status",
                table: "organization_member",
                sql: "status IN ('active', 'invited', 'suspended')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_organization_member_status",
                table: "organization_member");

            migrationBuilder.DropColumn(
                name: "status",
                table: "organization_member");
        }
    }
}
