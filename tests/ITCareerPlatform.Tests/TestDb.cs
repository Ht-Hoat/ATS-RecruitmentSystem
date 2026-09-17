using ITCareerPlatform.Data;
using ITCareerPlatform.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ITCareerPlatform.Tests;

// Tạo AppDbContext trên SQLite in-memory (giữ connection mở suốt vòng đời test).
// EnsureCreated() sẽ tự seed 3 vai trò (HasData) — nên RoleId 1/2/3 luôn hợp lệ.
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _conn;
    public AppDbContext Db { get; }

    public TestDb()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        _conn.CreateFunction("lower", (string? s) => s?.ToLower());
        Db = NewContext();
        Db.Database.EnsureCreated();
    }

    public AppDbContext NewContext()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        return new AppDbContext(opts);
    }

    // Tiện ích: thêm 1 user với vai trò cho trước, trả về Id.
    public User AddUser(string name, string email, int roleId, bool active = true)
    {
        var u = new User
        {
            FullName = name, Email = email, RoleId = roleId, IsActive = active,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("12345678")
        };
        Db.Users.Add(u); Db.SaveChanges();
        return u;
    }

    public Job AddJob(int createdBy, string title = "Backend .NET",
        string category = "Backend", string techStack = "C#,.NET,SQL Server",
        string level = "Junior", string status = "Open",
        string employmentType = "Onsite", string location = "Hà Nội",
        decimal salaryMin = 0, decimal salaryMax = 0)
    {
        var j = new Job
        {
            Title = title, Category = category, TechStack = techStack, Level = level,
            Status = status, CreatedById = createdBy,
            // P0-2: hạn nộp là một NGÀY trên tờ lịch Việt Nam; mốc tạo là một THỜI ĐIỂM ở UTC.
            // Dùng giờ máy chạy test cho cả hai thì kết quả đổi theo múi giờ của máy đó.
            Deadline = VietnamDateHelper.Today().AddDays(10),
            EmploymentType = employmentType, Location = location,
            SalaryMin = salaryMin, SalaryMax = salaryMax,
            CreatedAt = DateTime.UtcNow
        };
        Db.Jobs.Add(j); Db.SaveChanges();
        return j;
    }

    public CandidateProfile AddProfile(int userId, string tags = "C#,.NET,SQL Server", bool withCv = true)
    {
        var p = new CandidateProfile
        {
            UserId = userId, FullName = "SV Test", Email = "sv@test.vn",
            TechSkillTags = tags, Skills = tags,
            CvData = withCv ? System.Text.Encoding.UTF8.GetBytes("%PDF-1.4 CV test") : null,
            CvFileName = withCv ? "cv.pdf" : null, CvContentType = "application/pdf",
            CvUploadedAt = withCv ? DateTime.UtcNow : null
        };
        Db.CandidateProfiles.Add(p); Db.SaveChanges();
        return p;
    }

    // Đơn ứng tuyển dựng sẵn. Chỉ số (JobId, CandidateProfileId) là duy nhất, nên mỗi
    // hồ sơ chỉ nộp được một lần vào một tin — test nào cần nhiều đơn phải tạo thêm
    // hồ sơ hoặc thêm tin, không gọi lại hàm này với cùng cặp id.
    public Application AddApplication(int jobId, int profileId,
        string status = ApplicationStatus.Submitted,
        int? aiScore = null, int? hrScore = null, DateTime? appliedAt = null)
    {
        var a = new Application
        {
            JobId = jobId,
            CandidateProfileId = profileId,
            Status = status,
            AiScore = aiScore,
            HrScore = hrScore,
            AppliedAt = appliedAt ?? DateTime.UtcNow,
            CvFileNameSnapshot = "cv.pdf"
        };
        Db.Applications.Add(a); Db.SaveChanges();
        return a;
    }

    /// <summary>Sinh viên + hồ sơ đi kèm, mỗi lần gọi là một cặp user/hồ sơ mới.</summary>
    public CandidateProfile AddStudentWithProfile(string suffix, string tags = "C#,.NET")
    {
        var u = AddUser("SV " + suffix, $"sv{suffix}@itcp.vn", Roles.StudentId);
        return AddProfile(u.Id, tags);
    }

    /// <summary>
    /// Mốc UTC ứng với 00:00 giờ Việt Nam của hôm nay (+/- offsetDays). Cột AppliedAt lưu
    /// UTC còn biểu đồ gộp theo ngày Việt Nam, nên test phải nói rõ mình đang gieo cái nào.
    /// </summary>
    public static DateTime VietnamDayStartUtc(int offsetDays = 0) =>
        VietnamDateHelper.StartOfVietnamDayUtc(VietnamDateHelper.Today().AddDays(offsetDays));

    public void Dispose() { Db.Dispose(); _conn.Dispose(); }
}
