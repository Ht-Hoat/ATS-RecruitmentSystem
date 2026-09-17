using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITCareerPlatform.Migrations
{
    /// <inheritdoc />
    public partial class AddSelfCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SelfChecks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    JobId = table.Column<int>(type: "int", nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    Strengths = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Missing = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Roadmap = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SelfChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SelfChecks_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SelfChecks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SelfChecks_JobId",
                table: "SelfChecks",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_SelfChecks_UserId_CreatedAt",
                table: "SelfChecks",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SelfChecks_UserId_JobId_Id",
                table: "SelfChecks",
                columns: new[] { "UserId", "JobId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SelfChecks");
        }
    }
}
