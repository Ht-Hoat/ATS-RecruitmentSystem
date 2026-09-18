using Microsoft.EntityFrameworkCore;

namespace ITCareerPlatform.Data;

/// <summary>
/// Đưa một CSDL tạo bằng EnsureCreated() (nhánh main, trước khi có migration) về đúng trạng
/// thái "đã chạy InitialCreate", để Migrate() nối tiếp được các migration sau.
///
/// Không có bước này, Migrate() trên CSDL đó chạy lại InitialCreate từ đầu và dừng ngay ở
/// CREATE TABLE đầu tiên ("There is already an object named 'Notifications'") — ứng dụng
/// không khởi động được. Và cũng KHÔNG thể chỉ ghi tay dòng InitialCreate vào bảng lịch sử:
/// schema của main thiếu các cột mà InitialCreate có (Users.SecurityStamp...), nên các
/// migration sau sẽ chạy trên một schema khác với cái chúng giả định.
///
/// Chênh lệch giữa schema main và InitialCreate (các migration sau chỉ THÊM, không sửa cột cũ):
///   - Users.SecurityStamp (int NOT NULL)            → thêm, mặc định 0
///   - Applications.AiSource (nvarchar(20) NULL)      → thêm
///   - IX_Applications_Status, IX_Jobs_Status_Deadline → cột Status ở main là nvarchar(max),
///     SQL Server không index được, nên thu về nvarchar(30)/nvarchar(20) trước
///   - IX_AuditLogs_Timestamp                         → thêm
/// Các cột chuỗi khác ở main là nvarchar(max) thay vì nvarchar(N): giữ nguyên — chạy đúng,
/// chỉ không có trần độ dài ở tầng CSDL (tầng service đã kiểm độ dài). Hai cột thừa của main
/// (Applications.AiSummary, ApplicationStatusHistories.Note) đều NULL được nên để yên.
/// </summary>
public static class LegacySchemaBridge
{
    public const string InitialMigrationId = "20260907002925_InitialCreate";
    public const string EfProductVersion = "10.0.0";

    /// <summary>Trả true nếu vừa nối một CSDL cũ; false nếu không cần (CSDL mới hoặc đã có lịch sử migration).</summary>
    public static bool ApplyIfNeeded(AppDbContext db)
    {
        if (!db.Database.IsSqlServer()) return false;
        // Máy chủ mới tinh: CSDL chưa tồn tại thì không có gì để nối — để Migrate() tạo mới.
        // Thiếu dòng này, câu kiểm tra bên dưới mở kết nối vào một CSDL chưa có và app sập
        // ngay lần khởi động đầu tiên ("Cannot open database ... requested by the login").
        if (!db.Database.CanConnect()) return false;

        var isLegacy = db.Database.SqlQueryRaw<int>(
            "SELECT CASE WHEN OBJECT_ID(N'[dbo].[Users]', N'U') IS NOT NULL " +
            "AND OBJECT_ID(N'[dbo].[__EFMigrationsHistory]', N'U') IS NULL THEN 1 ELSE 0 END AS [Value]")
            .AsEnumerable().Single() == 1;
        if (!isLegacy) return false;

        db.Database.ExecuteSqlRaw(Script);
        return true;
    }

    // Một giao dịch: hỏng ở bất kỳ bước nào thì CSDL giữ nguyên như cũ, không bị bỏ dở giữa
    // chừng. CREATE INDEX đi qua EXEC để được biên dịch SAU khi ALTER COLUMN đã chạy.
    internal const string Script = $"""
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;

        IF EXISTS (SELECT 1 FROM [Applications] WHERE LEN([Status]) > 30)
            THROW 50001, N'Applications.Status có giá trị dài hơn 30 ký tự — không thu cột về nvarchar(30) được. Hãy sửa dữ liệu rồi khởi động lại.', 1;
        IF EXISTS (SELECT 1 FROM [Jobs] WHERE LEN([Status]) > 20)
            THROW 50002, N'Jobs.Status có giá trị dài hơn 20 ký tự — không thu cột về nvarchar(20) được. Hãy sửa dữ liệu rồi khởi động lại.', 1;

        IF COL_LENGTH(N'dbo.Users', N'SecurityStamp') IS NULL
            ALTER TABLE [Users] ADD [SecurityStamp] int NOT NULL CONSTRAINT [DF_Users_SecurityStamp] DEFAULT 0;

        IF COL_LENGTH(N'dbo.Applications', N'AiSource') IS NULL
            ALTER TABLE [Applications] ADD [AiSource] nvarchar(20) NULL;

        ALTER TABLE [Applications] ALTER COLUMN [Status] nvarchar(30) NOT NULL;
        ALTER TABLE [Jobs] ALTER COLUMN [Status] nvarchar(20) NOT NULL;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Applications_Status' AND object_id = OBJECT_ID(N'dbo.Applications'))
            EXEC(N'CREATE INDEX [IX_Applications_Status] ON [Applications] ([Status]);');
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Jobs_Status_Deadline' AND object_id = OBJECT_ID(N'dbo.Jobs'))
            EXEC(N'CREATE INDEX [IX_Jobs_Status_Deadline] ON [Jobs] ([Status], [Deadline]);');
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AuditLogs_Timestamp' AND object_id = OBJECT_ID(N'dbo.AuditLogs'))
            EXEC(N'CREATE INDEX [IX_AuditLogs_Timestamp] ON [AuditLogs] ([Timestamp]);');

        CREATE TABLE [__EFMigrationsHistory] (
            [MigrationId] nvarchar(150) NOT NULL,
            [ProductVersion] nvarchar(32) NOT NULL,
            CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
        );
        INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
        VALUES (N'{InitialMigrationId}', N'{EfProductVersion}');

        COMMIT TRANSACTION;
        """;
}
