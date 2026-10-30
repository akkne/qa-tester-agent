using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QaAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SessionNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence<long>(
                name: "run_session_number_seq",
                startValue: 100000L);

            migrationBuilder.AddColumn<long>(
                name: "SessionNumber",
                table: "runs",
                type: "bigint",
                nullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "SessionNumber",
                table: "runs",
                type: "bigint",
                nullable: false,
                defaultValueSql: "nextval('run_session_number_seq')",
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_runs_SessionNumber",
                table: "runs",
                column: "SessionNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_runs_SessionNumber",
                table: "runs");

            migrationBuilder.DropColumn(
                name: "SessionNumber",
                table: "runs");

            migrationBuilder.DropSequence(name: "run_session_number_seq");
        }
    }
}
