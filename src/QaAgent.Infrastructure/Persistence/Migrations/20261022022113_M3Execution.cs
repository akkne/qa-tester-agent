using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QaAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M3Execution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "M3ResultJson",
                table: "runs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "M3ResultJson",
                table: "runs");
        }
    }
}
