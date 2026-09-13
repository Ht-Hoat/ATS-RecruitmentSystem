using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITCareerPlatform.Migrations
{
    /// <inheritdoc />
    public partial class AddMentorNotesAiQuestionsInterviewAndExperience : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "YearsOfExperience",
                table: "CandidateProfiles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AiQuestions",
                table: "Applications",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AiQuestionsAt",
                table: "Applications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiQuestionsSource",
                table: "Applications",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InternalNote",
                table: "Applications",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InternalNoteAt",
                table: "Applications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InternalNoteByUserId",
                table: "Applications",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InterviewAt",
                table: "Applications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InterviewLink",
                table: "Applications",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InterviewNote",
                table: "Applications",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "YearsOfExperience",
                table: "CandidateProfiles");

            migrationBuilder.DropColumn(
                name: "AiQuestions",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "AiQuestionsAt",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "AiQuestionsSource",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "InternalNote",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "InternalNoteAt",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "InternalNoteByUserId",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "InterviewAt",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "InterviewLink",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "InterviewNote",
                table: "Applications");
        }
    }
}
