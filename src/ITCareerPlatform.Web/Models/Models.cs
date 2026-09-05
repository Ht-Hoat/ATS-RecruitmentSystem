namespace ITCareerPlatform.Models;

// =====================================================================
//  ENTITIES — IT Career Platform (bám sát ERD & Class Diagram v2)
//  Vai trò: Admin (1) · Mentor/HR IT (2) · SinhVienIT (3)
// =====================================================================

/// <summary>Hằng số tên vai trò — dùng chung cho [Authorize(Roles=...)] và so sánh claim.</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Mentor = "Mentor";         // Mentor / HR IT
    public const string Student = "SinhVienIT";    // Sinh viên IT

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

public class Role
{
    public int Id { get; set; }
    public string RoleName { get; set; } = "";
    public string Description { get; set; } = "";
    public ICollection<User> Users { get; set; } = new List<User>();
}

public class User
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public int RoleId { get; set; }
    public Role? Role { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
    public ICollection<Job> CreatedJobs { get; set; } = new List<Job>();
}

public class Job
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";   // Mô tả công việc (JD)
    public string Requirements { get; set; } = "";
    public string Location { get; set; } = "";
    public decimal SalaryMin { get; set; }
    public decimal SalaryMax { get; set; }
    public DateTime Deadline { get; set; }
    public string Status { get; set; } = "Open";     // Open | Closed

    // ===== ATS-04.1: 3 trường IT mới =====
    public string Category { get; set; } = "Khác";   // Backend, Frontend, Mobile, DevOps, Data/AI, QA, Design, Khác
    public string TechStack { get; set; } = "";      // "C#, .NET, SQL Server, Docker"
    public string Level { get; set; } = "Junior";    // Intern, Junior, Middle, Senior

    public int CreatedById { get; set; }
    public User? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<Application> Applications { get; set; } = new List<Application>();

    /// <summary>Danh mục IT hợp lệ (dùng cho dropdown + filter).</summary>
    public static readonly string[] Categories =
        { "Backend", "Frontend", "Mobile", "DevOps", "Data/AI", "QA", "Design", "Khác" };

    public static readonly string[] Levels = { "Intern", "Junior", "Middle", "Senior" };
}

public class AuditLog
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public string Action { get; set; } = "";
    public string TableName { get; set; } = "";
    public string Details { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

// ===== ATS-08: Hồ sơ Sinh viên IT + ATS-09: CV =====
public class CandidateProfile
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    // Thông tin cá nhân
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public DateTime? DateOfBirth { get; set; }
    public string Address { get; set; } = "";
    public string Education { get; set; } = "";
    public string Experience { get; set; } = "";
    public string Skills { get; set; } = "";

    // ===== ATS-08.1: 4 trường IT chuyên sâu mới =====
    public string GithubUrl { get; set; } = "";
    public string LinkedInUrl { get; set; } = "";
    public string PortfolioUrl { get; set; } = "";
    public string TechSkillTags { get; set; } = "";   // "C#,React,Docker" — phân cách bằng dấu phẩy

    // CV (PDF/DOCX, tối đa 5MB)
    public byte[]? CvData { get; set; }
    public string? CvFileName { get; set; }
    public string? CvContentType { get; set; }
    public DateTime? CvUploadedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<Application> Applications { get; set; } = new List<Application>();

    public bool HasCv => CvData != null && CvData.Length > 0;

    /// <summary>Tách chuỗi tags thành danh sách chip.</summary>
    public IEnumerable<string> SkillTagList =>
        (TechSkillTags ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

// ===== ATS-10: Đơn ứng tuyển =====
public class Application
{
    public int Id { get; set; }

    public int JobId { get; set; }
    public Job? Job { get; set; }

    public int CandidateProfileId { get; set; }
    public CandidateProfile? CandidateProfile { get; set; }

    // ===== Đóng băng CV tại thời điểm nộp (bản chụp, không đổi khi SV cập nhật CV sau này) =====
    public string CvFileNameSnapshot { get; set; } = "";
    public byte[]? CvDataSnapshot { get; set; }              // byte[] của CV lúc nộp
    public string? CvContentTypeSnapshot { get; set; }       // content-type lúc nộp
    public bool HasCvSnapshot => CvDataSnapshot != null && CvDataSnapshot.Length > 0;

    // Đã nộp | Đang xem xét | Phỏng vấn | Trúng tuyển | Từ chối
    public string Status { get; set; } = ApplicationStatus.Submitted;

    public DateTime AppliedAt { get; set; } = DateTime.Now;

    // ===== ATS-13/14: Đánh giá độ phù hợp & Gợi ý lộ trình (AI cố vấn hướng nghiệp) =====
    public int? AiScore { get; set; }          // % phù hợp (0-100), null nếu chưa đánh giá
    public string? AiStrengths { get; set; }   // ✅ Điểm mạnh phù hợp với JD
    public string? AiMissing { get; set; }     // ❌ Kỹ năng còn thiếu
    public string? AiRoadmap { get; set; }     // 🚀 Lộ trình tự học ngắn hạn
    public string? AiSummary { get; set; }     // Bản text gốc AI trả về (lưu để đối chiếu)
    public DateTime? AiScoredAt { get; set; }
    public int? HrScore { get; set; }          // % Mentor điều chỉnh (ATS-16)
    public string? HrNote { get; set; }        // Lý do điều chỉnh
    public DateTime? HrAdjustedAt { get; set; }

    // ATS-16.2: % dùng để xếp hạng = HrScore nếu có, ngược lại AiScore
    public int? FinalScore => HrScore ?? AiScore;
    public bool HasAiEvaluation => AiScore.HasValue;

    public ICollection<ApplicationStatusHistory> StatusHistory { get; set; } = new List<ApplicationStatusHistory>();
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
    public string FromStatus { get; set; } = "";
    public string ToStatus { get; set; } = "";
    public int ChangedByUserId { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.Now;
    public string? Note { get; set; }
}

// ===== NTF-01: Thông báo cho Sinh viên IT =====
public class Notification
{
    public int Id { get; set; }
    public int UserId { get; set; }               // người nhận
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string Link { get; set; } = "/my-applications";
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
