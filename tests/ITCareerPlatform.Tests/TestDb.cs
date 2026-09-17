using ITCareerPlatform.Data;
using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ITCareerPlatform.Tests;

// Tạo AppDbContext trên SQLite in-memory (giữ connection mở suốt vòng đời test).
// EnsureCreated() sẽ tự seed 3 vai trò (HasData) — nên RoleId 1/2/3 luôn hợp lệ.
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly string _cvRoot;
    public AppDbContext Db { get; }

    /// <summary>
    /// P2-2: nơi lưu nội dung CV cho test — một thư mục tạm RIÊNG cho mỗi TestDb, xóa khi
    /// Dispose. Dùng chung một thư mục thì phần khử trùng lặp theo hash làm các test nhìn
    /// thấy tệp của nhau và kết quả phụ thuộc thứ tự chạy.
    /// </summary>
    public ICvStorage CvStorage { get; }

    /// <summary>Thư mục lưu CV của riêng TestDb này — để test đếm số tệp thật sự nằm trên đĩa.</summary>
    public string CvRoot => _cvRoot;

    public TestDb()
    {
        _cvRoot = Path.Combine(Path.GetTempPath(), "itcp-cv-test", Guid.NewGuid().ToString("N"));
        CvStorage = new DiskCvStorage(_cvRoot);

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

    /// <summary>
    /// P1-1: công ty mặc định cho những test không quan tâm tới công ty.
    ///
    /// Jobs.CompanyId là NOT NULL kèm khóa ngoại, nên mọi tin đều phải thuộc về một công ty
    /// có thật. Tạo sẵn đúng một công ty dùng chung thay vì bắt hàng chục test hiện có phải
    /// khai báo lại — test nào THỰC SỰ kiểm chuyện công ty thì tự dựng công ty riêng.
    /// </summary>
    public Company DefaultCompany => _defaultCompany ??= CreateDefaultCompany();
    private Company? _defaultCompany;

    private Company CreateDefaultCompany()
    {
        var c = new Company { Name = "Công ty mặc định (test)" };
        Db.Companies.Add(c); Db.SaveChanges();
        return c;
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

    /// <summary>Mentor đã được gán công ty — đủ điều kiện đăng tin qua JobService.Create.</summary>
    public User AddMentor(string name = "M", string email = "m@itcp.vn", int? companyId = null)
    {
        var u = AddUser(name, email, Roles.MentorId);
        u.CompanyId = companyId ?? DefaultCompany.Id;
        Db.SaveChanges();
        return u;
    }

    public Job AddJob(int createdBy, string title = "Backend .NET",
        string category = "Backend", string techStack = "C#,.NET,SQL Server",
        string level = "Junior", string status = "Open",
        string employmentType = "Onsite", string location = "Hà Nội",
        decimal salaryMin = 0, decimal salaryMax = 0, int? companyId = null)
    {
        var j = new Job
        {
            CompanyId = companyId ?? DefaultCompany.Id,
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

    /// <param name="withAiConsent">
    /// P2-3: mặc định ĐÃ đồng ý cho gửi CV tới dịch vụ AI, vì phần lớn test nói về chuyện
    /// khác và một hồ sơ chưa đồng ý sẽ bị chặn ở mọi đường gọi AI. Test nào thực sự kiểm
    /// chốt chặn đó thì truyền false.
    /// </param>
    public CandidateProfile AddProfile(int userId, string tags = "C#,.NET,SQL Server", bool withCv = true,
        bool withAiConsent = true)
    {
        var p = new CandidateProfile
        {
            UserId = userId, FullName = "SV Test", Email = "sv@test.vn",
            TechSkillTags = tags, Skills = tags,
            AiConsentAt = withAiConsent ? DateTime.UtcNow : null,
            AiConsentVersion = withAiConsent ? CandidateProfile.CurrentAiConsentVersion : null,
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

    public void Dispose()
    {
        Db.Dispose();
        _conn.Dispose();
        // Dọn thư mục CV tạm. Xóa hỏng (tệp đang bị khóa) không được làm đỏ test — thư mục
        // nằm trong TEMP nên hệ điều hành sẽ dọn sau.
        try { if (Directory.Exists(_cvRoot)) Directory.Delete(_cvRoot, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
