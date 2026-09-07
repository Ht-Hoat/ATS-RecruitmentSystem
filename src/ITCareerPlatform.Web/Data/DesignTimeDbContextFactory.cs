using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ITCareerPlatform.Data;

/// <summary>
/// Cho phép <c>dotnet ef migrations add</c> dựng được AppDbContext mà KHÔNG chạy Program.cs.
/// Nếu thiếu lớp này, công cụ EF sẽ gọi vào entry point của ứng dụng, và đoạn khởi động ở đó
/// mở kết nối CSDL rồi nạp dữ liệu mẫu — nghĩa là không tạo được migration khi chưa có
/// máy chủ SQL đang chạy.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Server=localhost;Database=ITCareerPlatform;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
