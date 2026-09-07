using System.ComponentModel.DataAnnotations;

namespace ITCareerPlatform.Models;

// =====================================================================
//  ENTITIES — IT Career Platform (bám sát ERD & Class Diagram v2)
//  Vai trò: Admin (1) · Mentor/HR IT (2) · SinhVienIT (3)
//
//  Ràng buộc dữ liệu khai báo bằng DataAnnotations: EF Core dùng chúng để
//  sinh cột nvarchar(n) NOT NULL thay vì nvarchar(max), và tầng endpoint
//  dùng Validator.TryValidateObject để kiểm tra lại phía server — form HTML
//  chỉ là gợi ý cho trình duyệt, không phải nơi thực thi luật.
// =====================================================================

/// <summary>Hằng số tên vai trò — dùng chung cho [Authorize(Roles=...)] và so sánh claim.</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Mentor = "Mentor";         // Mentor / HR IT
    public const string Student = "SinhVienIT";    // Sinh viên IT

    /// <summary>Dùng cho [Authorize(Roles = Roles.AdminOrMentor)] — phải là const để làm attribute argument.</summary>
    public const string AdminOrMentor = Admin + "," + Mentor;

    public const int AdminId = 1;
    public const int MentorId = 2;
    public const int StudentId = 3;

    /// <summary>Nhãn tiếng Việt để hiển thị UI.</summary>
    public static string Display(string role) => role switch
    {
        Admin => "Admin",
        Mentor => "Mentor / HR IT",
        Student => "Sinh viên IT",
        _ => role
    };
}

/// <summary>Trạng thái tin tuyển dụng — trước đây là chuỗi "Open"/"Closed" rải rác 20 chỗ.</summary>
public static class JobStatus
{
    public const string Open = "Open";
    public const string Closed = "Closed";

    public static readonly string[] All = { Open, Closed };
    public static bool IsValid(string? s) => s is not null && All.Contains(s);
}

/// <summary>Mốc thời gian do <c>AppDbContext.SaveChanges</c> tự đóng dấu — không gán tay ở từng service.</summary>
public interface ITimestamped
{
    DateTime CreatedAt { get; set; }
    DateTime? UpdatedAt { get; set; }
}

public class Role
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string RoleName { get; set; } = "";

    [MaxLength(200)]
    public string Description { get; set; } = "";

    public ICollection<User> Users { get; set; } = new List<User>();
}

public class User : ITimestamped
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Họ tên không được để trống."), MaxLength(120)]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = "Email không được để trống.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    [MaxLength(160)]
    public string Email { get; set; } = "";

    [Required, MaxLength(200)]
    public string PasswordHash { get; set; } = "";

    public int RoleId { get; set; }
    public Role? Role { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Tăng lên mỗi khi khóa/mở khóa hoặc đổi vai trò. Cookie đăng nhập lưu giá trị này;
    /// mỗi request cookie được đối chiếu lại với DB, nên thao tác của Admin có hiệu lực ngay
    /// thay vì phải đợi cookie hết hạn (14 ngày).
    /// </summary>
    public int SecurityStamp { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
    public ICollection<Job> CreatedJobs { get; set; } = new List<Job>();
}

public class Job : ITimestamped
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Tiêu đề công việc không được để trống."), MaxLength(160)]
    public string Title { get; set; } = "";

    [MaxLength(4000)]
    public string Description { get; set; } = "";   // Mô tả công việc (JD)

    [MaxLength(4000)]
    public string Requirements { get; set; } = "";

    [MaxLength(120)]
    public string Location { get; set; } = "";

    [Range(0, 10_000, ErrorMessage = "Lương tối thiểu không hợp lệ.")]
    public decimal SalaryMin { get; set; }

    [Range(0, 10_000, ErrorMessage = "Lương tối đa không hợp lệ.")]
    public decimal SalaryMax { get; set; }

    public DateTime Deadline { get; set; }

    [Required, MaxLength(20)]
    public string Status { get; set; } = JobStatus.Open;

    // ===== ATS-04.1: 3 trường IT =====
    [Required, MaxLength(40)]
    public string Category { get; set; } = "Khác";   // phải thuộc Job.Categories

    [MaxLength(400)]
    public string TechStack { get; set; } = "";      // "C#, .NET, SQL Server, Docker"

    [Required, MaxLength(20)]
    public string Level { get; set; } = "Junior";    // phải thuộc Job.Levels

    public int CreatedById { get; set; }
    public User? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<Application> Applications { get; set; } = new List<Application>();

    /// <summary>Danh sách công nghệ yêu cầu, đã tách khỏi chuỗi phân cách bằng dấu phẩy.</summary>
    public IReadOnlyList<string> TechStackList => TechList.Parse(TechStack);

    /// <summary>Danh mục IT hợp lệ (dùng cho dropdown + filter + validate phía server).</summary>
    public static readonly string[] Categories =
        { "Backend", "Frontend", "Mobile", "DevOps", "Data/AI", "QA", "Design", "Khác" };

    public static readonly string[] Levels = { "Intern", "Junior", "Middle", "Senior" };

    public static bool IsValidCategory(string? c) => c is not null && Categories.Contains(c);
    public static bool IsValidLevel(string? l) => l is not null && Levels.Contains(l);
}

public class AuditLog
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }

    [Required, MaxLength(60)]
    public string Action { get; set; } = "";

    [Required, MaxLength(60)]
    public string TableName { get; set; } = "";

    [MaxLength(500)]
    public string Details { get; set; } = "";

    public DateTime Timestamp { get; set; } = DateTime.Now;
}

// ===== ATS-08: Hồ sơ Sinh viên IT + ATS-09: CV =====
public class CandidateProfile : ITimestamped
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    // Thông tin cá nhân
    [Required(ErrorMessage = "Họ tên không được để trống."), MaxLength(120)]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = "Email không được để trống.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    [MaxLength(160)]
    public string Email { get; set; } = "";

    // Không gắn [Phone]: attribute đó coi chuỗi rỗng là không hợp lệ, trong khi số điện thoại
    // là trường tùy chọn. Định dạng được kiểm tra ở ProfileService, chỉ khi có nhập.
    [MaxLength(20)]
    public string Phone { get; set; } = "";

    public DateTime? DateOfBirth { get; set; }

    [MaxLength(250)] public string Address { get; set; } = "";
    [MaxLength(1000)] public string Education { get; set; } = "";
    [MaxLength(2000)] public string Experience { get; set; } = "";
    [MaxLength(2000)] public string Skills { get; set; } = "";

    // ===== ATS-08.1: 4 trường IT chuyên sâu =====
    [MaxLength(250)] public string GithubUrl { get; set; } = "";
    [MaxLength(250)] public string LinkedInUrl { get; set; } = "";
    [MaxLength(250)] public string PortfolioUrl { get; set; } = "";
    [MaxLength(500)] public string TechSkillTags { get; set; } = "";   // "C#,React,Docker"

    // CV (PDF/DOCX, tối đa 5MB)
    public byte[]? CvData { get; set; }
    [MaxLength(260)] public string? CvFileName { get; set; }
    [MaxLength(120)] public string? CvContentType { get; set; }
    public DateTime? CvUploadedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<Application> Applications { get; set; } = new List<Application>();

    public bool HasCv => CvData != null && CvData.Length > 0;

    /// <summary>Tách chuỗi tags thành danh sách chip.</summary>
    public IReadOnlyList<string> SkillTagList => TechList.Parse(TechSkillTags);
}

// ===== ATS-10: Đơn ứng tuyển =====
public class Application
{
    public int Id { get; set; }

    public int JobId { get; set; }
    public Job? Job { get; set; }

    public int CandidateProfileId { get; set; }
    public CandidateProfile? CandidateProfile { get; set; }

    // ===== Đóng băng CV tại thời điểm nộp =====
    [MaxLength(260)] public string CvFileNameSnapshot { get; set; } = "";
    public byte[]? CvDataSnapshot { get; set; }
    [MaxLength(120)] public string? CvContentTypeSnapshot { get; set; }
    public bool HasCvSnapshot => CvDataSnapshot != null && CvDataSnapshot.Length > 0;

    // Đã nộp | Đang xem xét | Phỏng vấn | Trúng tuyển | Từ chối
    [Required, MaxLength(30)]
    public string Status { get; set; } = ApplicationStatus.Submitted;

    public DateTime AppliedAt { get; set; } = DateTime.Now;

    // ===== ATS-13/14: Đánh giá độ phù hợp & Gợi ý lộ trình =====
    public int? AiScore { get; set; }          // % phù hợp (0-100), null nếu chưa đánh giá
    [MaxLength(1000)] public string? AiStrengths { get; set; }
    [MaxLength(1000)] public string? AiMissing { get; set; }
    [MaxLength(1000)] public string? AiRoadmap { get; set; }

    /// <summary>
    /// Nguồn của điểm: mô hình AI thật hay công thức đối chiếu offline.
    /// Được hiển thị trên giao diện — trước đây hai nguồn không phân biệt được,
    /// nên một lần Gemini lỗi trông y hệt một lần chấm thành công.
    /// </summary>
    [MaxLength(20)] public string? AiSource { get; set; }

    public DateTime? AiScoredAt { get; set; }

    public int? HrScore { get; set; }          // % Mentor điều chỉnh (ATS-16)
    [MaxLength(500)] public string? HrNote { get; set; }
    public DateTime? HrAdjustedAt { get; set; }

    // ATS-16.2: % dùng để xếp hạng = HrScore nếu có, ngược lại AiScore
    public int? FinalScore => HrScore ?? AiScore;
    public bool HasAiEvaluation => AiScore.HasValue;
    public bool IsOfflineEvaluation => AiSource == EvaluationSource.Offline;

    public ICollection<ApplicationStatusHistory> StatusHistory { get; set; } = new List<ApplicationStatusHistory>();
}

/// <summary>Nguồn sinh ra điểm phù hợp — quyết định nhãn hiển thị cho Mentor và Sinh viên.</summary>
public static class EvaluationSource
{
    public const string Gemini = "Gemini";
    public const string Offline = "Offline";
}

/// <summary>5 trạng thái xử lý hồ sơ (ATS-17).</summary>
public static class ApplicationStatus
{
    public const string Submitted = "Đã nộp";
    public const string Reviewing = "Đang xem xét";
    public const string Interview = "Phỏng vấn";
    public const string Accepted = "Trúng tuyển";
    public const string Rejected = "Từ chối";

    public static readonly string[] All = { Submitted, Reviewing, Interview, Accepted, Rejected };
}

// ===== ATS-17.2: Lịch sử thay đổi trạng thái đơn =====
public class ApplicationStatusHistory
{
    public int Id { get; set; }
    public int ApplicationId { get; set; }
    public Application? Application { get; set; }

    [Required, MaxLength(30)] public string FromStatus { get; set; } = "";
    [Required, MaxLength(30)] public string ToStatus { get; set; } = "";

    public int ChangedByUserId { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.Now;
}

// ===== NTF-01: Thông báo cho Sinh viên IT =====
public class Notification
{
    public int Id { get; set; }
    public int UserId { get; set; }               // người nhận

    [Required, MaxLength(160)] public string Title { get; set; } = "";
    [MaxLength(500)] public string Message { get; set; } = "";

    /// <summary>Đường dẫn nội bộ. Chỉ giá trị lưu ở đây mới được dùng để chuyển trang.</summary>
    [MaxLength(250)] public string Link { get; set; } = "/my-applications";

    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

// =====================================================================
//  Tách danh sách công nghệ khỏi chuỗi "C#, .NET, SQL Server"
//  Một chỗ duy nhất — trước đây luật tách được gõ lại ở 3 trang Razor,
//  1 property model và 1 đoạn JavaScript, và bản JS đã lệch với bản C#.
// =====================================================================
public static class TechList
{
    private static readonly char[] Separators = { ',', ';', '|', '\n', '\r' };

    /// <summary>Tách, bỏ khoảng trắng thừa và mục rỗng; giữ nguyên cụm nhiều từ ("SQL Server").</summary>
    public static IReadOnlyList<string> Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
        return raw.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Where(s => s.Length > 0)
                  .ToList();
    }

    /// <summary>Dạng chuẩn hóa để so khớp: chữ thường, gộp khoảng trắng. "SQL  Server" == "sql server".</summary>
    public static string Normalize(string item) =>
        string.Join(' ', item.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static HashSet<string> NormalizedSet(string? raw) =>
        Parse(raw).Select(Normalize).ToHashSet();
}
