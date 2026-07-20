using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SessionOrganizationScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing sessions predate the workspace scope and there is nothing to backfill from:
            // the token records who signed in, never where they were working. EF's generated default
            // of an all-zero uuid would violate the foreign key below on the first existing row, so
            // the sessions are ended instead and everyone signs in again.
            //
            // Deliberately not a `defaultValue` on the column either. A default that is not a real
            // organization is a foreign-key violation waiting for the first insert that forgets the
            // column — better that such an insert fails loudly at the NOT NULL.
            migrationBuilder.Sql("DELETE FROM refresh_token;");

            migrationBuilder.AddColumn<Guid>(
                name: "organization_id",
                table: "refresh_token",
                type: "uuid",
                nullable: false);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_token_organization_id",
                table: "refresh_token",
                column: "organization_id");

            migrationBuilder.AddForeignKey(
                name: "fk_refresh_token_organization_organization_id",
                table: "refresh_token",
                column: "organization_id",
                principalTable: "organization",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_refresh_token_organization_organization_id",
                table: "refresh_token");

            migrationBuilder.DropIndex(
                name: "ix_refresh_token_organization_id",
                table: "refresh_token");

            migrationBuilder.DropColumn(
                name: "organization_id",
                table: "refresh_token");
        }
    }
}
