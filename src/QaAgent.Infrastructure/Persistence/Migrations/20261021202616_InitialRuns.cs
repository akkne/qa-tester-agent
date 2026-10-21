using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QaAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepositoryId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BaseSha = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    HeadSha = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ProfileVersion = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FinalStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    ChangedFilesJson = table.Column<string>(type: "text", nullable: true),
                    Diff = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_runs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_runs_RepositoryId_BaseSha_HeadSha_ProfileVersion",
                table: "runs",
                columns: new[] { "RepositoryId", "BaseSha", "HeadSha", "ProfileVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_runs_State_CreatedAt",
                table: "runs",
                columns: new[] { "State", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "runs");
        }
    }
}
