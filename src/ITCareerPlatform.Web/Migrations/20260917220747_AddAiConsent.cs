using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITCareerPlatform.Migrations
{
    /// <inheritdoc />
    public partial class AddAiConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AiConsentAt",
                table: "CandidateProfiles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiConsentVersion",
                table: "CandidateProfiles",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiConsentAt",
                table: "CandidateProfiles");

            migrationBuilder.DropColumn(
                name: "AiConsentVersion",
                table: "CandidateProfiles");
        }
    }
}
