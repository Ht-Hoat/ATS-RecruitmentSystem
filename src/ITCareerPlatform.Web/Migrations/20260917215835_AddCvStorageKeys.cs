using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITCareerPlatform.Migrations
{
    /// <inheritdoc />
    public partial class AddCvStorageKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CvStorageKey",
                table: "CandidateProfiles",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CvStorageKeySnapshot",
                table: "Applications",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CvStorageKey",
                table: "CandidateProfiles");

            migrationBuilder.DropColumn(
                name: "CvStorageKeySnapshot",
                table: "Applications");
        }
    }
}
