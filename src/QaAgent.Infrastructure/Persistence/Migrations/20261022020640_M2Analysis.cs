using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QaAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M2Analysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "M2ResultJson",
                table: "runs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "M2ResultJson",
                table: "runs");
        }
    }
}
