using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITCareerPlatform.Migrations
{
    /// <inheritdoc />
    public partial class AddHrScoreByUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HrScoreByUserId",
                table: "Applications",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HrScoreByUserId",
                table: "Applications");
        }
    }
}
