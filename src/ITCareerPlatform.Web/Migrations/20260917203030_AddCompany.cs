using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITCareerPlatform.Migrations
{
    /// <inheritdoc />
    public partial class AddCompany : Migration
    {
        /// <summary>
        /// P1-1: thêm bảng Companies và cột CompanyId cho Jobs/Users.
        ///
        /// Thứ tự ở đây là thứ tự BẮT BUỘC, không phải thứ tự EF sinh ra mặc định: EF đặt
        /// khóa ngoại ngay sau khi thêm cột, nên trên một CSDL đang có tin tuyển dụng thì
        /// mọi dòng mang CompanyId = 0 và lệnh thêm FK hỏng ngay giữa chừng — nâng cấp
        /// thất bại, CSDL nằm lại ở trạng thái nửa vời. Vì vậy: tạo bảng → tạo công ty mặc
        /// định → thêm cột → ĐẮP DỮ LIỆU → rồi mới ràng buộc.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Website = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Companies_Name",
                table: "Companies",
                column: "Name");

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "Users",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "Jobs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Công ty mặc định chỉ được tạo khi THỰC SỰ có dữ liệu cũ cần gán. Trên một CSDL
            // mới tinh (Jobs và Users còn rỗng vì SeedData chạy sau Migrate) thì không sinh
            // ra bản ghi rác nào — danh sách công ty của Admin bắt đầu từ chỗ trống.
            // RoleId 1 = Admin, 2 = Mentor (xem Roles trong Models.cs) — hai vai trò đăng
            // được tin, nên cả hai đều cần công ty.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [Jobs]) OR EXISTS (SELECT 1 FROM [Users] WHERE [RoleId] IN (1, 2))
BEGIN
    IF NOT EXISTS (SELECT 1 FROM [Companies] WHERE [Name] = N'Chưa cập nhật')
        INSERT INTO [Companies] ([Name], [Website], [Description], [Address], [CreatedAt])
        VALUES (N'Chưa cập nhật', N'', N'Công ty mặc định do bản nâng cấp tạo ra. Hãy sửa lại thông tin thật hoặc gán tin sang công ty đúng.', N'', SYSUTCDATETIME());

    DECLARE @placeholder INT = (SELECT TOP 1 [Id] FROM [Companies] WHERE [Name] = N'Chưa cập nhật');

    UPDATE [Jobs] SET [CompanyId] = @placeholder WHERE [CompanyId] = 0;
    UPDATE [Users] SET [CompanyId] = @placeholder WHERE [CompanyId] IS NULL AND [RoleId] IN (1, 2);
END
");

            migrationBuilder.CreateIndex(
                name: "IX_Users_CompanyId",
                table: "Users",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_CompanyId",
                table: "Jobs",
                column: "CompanyId");

            migrationBuilder.AddForeignKey(
                name: "FK_Jobs_Companies_CompanyId",
                table: "Jobs",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Companies_CompanyId",
                table: "Users",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Jobs_Companies_CompanyId",
                table: "Jobs");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Companies_CompanyId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "Companies");

            migrationBuilder.DropIndex(
                name: "IX_Users_CompanyId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_CompanyId",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Jobs");
        }
    }
}
