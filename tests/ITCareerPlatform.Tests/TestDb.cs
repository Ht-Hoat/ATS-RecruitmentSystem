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
        string level = "Junior", string status = "Open")
    {
        var j = new Job
        {
            Title = title, Category = category, TechStack = techStack, Level = level,
            Status = status, CreatedById = createdBy, Deadline = DateTime.Today.AddDays(10),
            CreatedAt = DateTime.Now
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
            CvUploadedAt = withCv ? DateTime.Now : null
        };
        Db.CandidateProfiles.Add(p); Db.SaveChanges();
        return p;
    }

    public void Dispose() { Db.Dispose(); _conn.Dispose(); }
}
