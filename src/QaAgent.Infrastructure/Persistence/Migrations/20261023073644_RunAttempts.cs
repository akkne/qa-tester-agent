using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QaAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RunAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_runs_RepositoryId_BaseSha_HeadSha_ProfileVersion",
                table: "runs");

            migrationBuilder.AddColumn<int>(
                name: "Attempt",
                table: "runs",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_runs_RepositoryId_BaseSha_HeadSha_ProfileVersion_Attempt",
                table: "runs",
                columns: new[] { "RepositoryId", "BaseSha", "HeadSha", "ProfileVersion", "Attempt" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_runs_RepositoryId_BaseSha_HeadSha_ProfileVersion_Attempt",
                table: "runs");

            migrationBuilder.DropColumn(
                name: "Attempt",
                table: "runs");

            migrationBuilder.CreateIndex(
                name: "IX_runs_RepositoryId_BaseSha_HeadSha_ProfileVersion",
                table: "runs",
                columns: new[] { "RepositoryId", "BaseSha", "HeadSha", "ProfileVersion" },
                unique: true);
        }
    }
}
