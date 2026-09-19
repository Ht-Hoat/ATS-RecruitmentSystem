using ITCareerPlatform.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITCareerPlatform.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260919154000_AddEmploymentTypeToJobs")]
public partial class AddEmploymentTypeToJobs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Bảo đảm migration chạy được trên database cũ do EnsureCreated tạo ra.
        migrationBuilder.Sql("""
            IF COL_LENGTH(N'dbo.Jobs', N'EmploymentType') IS NULL
            BEGIN
                ALTER TABLE [dbo].[Jobs]
                ADD [EmploymentType] nvarchar(20) NOT NULL
                    CONSTRAINT [DF_Jobs_EmploymentType] DEFAULT N'Onsite' WITH VALUES;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH(N'dbo.Jobs', N'EmploymentType') IS NOT NULL
            BEGIN
                ALTER TABLE [dbo].[Jobs] DROP CONSTRAINT [DF_Jobs_EmploymentType];
                ALTER TABLE [dbo].[Jobs] DROP COLUMN [EmploymentType];
            END
            """);
    }
}
