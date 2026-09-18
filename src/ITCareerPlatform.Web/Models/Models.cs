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

    /// <summary>Ba vai trò theo thứ tự hiển thị — để không phải gõ lại danh sách khi duyệt vai trò.</summary>
    public static readonly int[] AllIds = { AdminId, MentorId, StudentId };

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

/// <summary>
/// P1-1: công ty đứng tên tin tuyển dụng.
///
/// Đây là thực thể trung tâm của một hệ thống tuyển dụng mà hệ thống này đang thiếu: trước
/// bản này Job chỉ có CreatedById trỏ tới một tài khoản Mentor, nên sinh viên đọc
/// "Backend Developer · Hà Nội · 15-25tr" mà không biết mình đang ứng tuyển cho ai.
/// </summary>
public class Company : ITimestamped
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Tên công ty không được để trống.")]
    [MaxLength(160, ErrorMessage = "Tên công ty tối đa 160 ký tự.")]
    public string Name { get; set; } = "";

    // Website để trống là hợp lệ; có nhập thì phải là https. Luật này được ProfileService
    // áp cho URL GitHub/LinkedIn theo đúng cách, nên ở đây dùng lại cùng một quy ước.
    [MaxLength(250, ErrorMessage = "Website tối đa 250 ký tự.")]
    public string Website { get; set; } = "";

    [MaxLength(2000, ErrorMessage = "Mô tả công ty tối đa 2000 ký tự.")]
    public string Description { get; set; } = "";

    [MaxLength(250, ErrorMessage = "Địa chỉ tối đa 250 ký tự.")]
    public string Address { get; set; } = "";

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ICollection<Job> Jobs { get; set; } = new List<Job>();

    /// <summary>
    /// Tên công ty mặc định mà migration gán cho mọi tin và mọi Mentor đang có.
    /// Không để cột CompanyId nullable chỉ vì dữ liệu cũ: null sẽ lan ra mọi truy vấn hiển
    /// thị và mọi trang phải tự xử lý trường hợp "chưa có công ty" một lần nữa.
    /// </summary>
    public const string PlaceholderName = "Chưa cập nhật";
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

    /// <summary>P0-3: Buộc người dùng đổi mật khẩu ở lần đăng nhập tiếp theo khi Admin reset.</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>
    /// P1-1: công ty của tài khoản — chỉ có nghĩa với vai trò Mentor/HR. Nullable vì Admin
    /// và Sinh viên IT không thuộc công ty nào, và một Mentor mới tạo chưa được gán ngay.
    /// Mentor chưa có công ty thì không đăng tin được (JobService từ chối kèm lý do rõ ràng).
    /// </summary>
    public int? CompanyId { get; set; }
    public Company? Company { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public ICollection<Job> CreatedJobs { get; set; } = new List<Job>();
}

public class Job : ITimestamped
{
    public int Id { get; set; }

    // Mọi MaxLength đều kèm ErrorMessage tiếng Việt: JobService.Validate chạy
    // Validator.TryValidateObject, nên thuộc tính nào không có câu riêng sẽ sinh ra câu
    // tiếng Anh tự động và hiện thẳng lên màn hình cho người đăng tin đọc.
    [Required(ErrorMessage = "Tiêu đề công việc không được để trống.")]
    [MaxLength(160, ErrorMessage = "Tiêu đề công việc tối đa 160 ký tự.")]
    public string Title { get; set; } = "";

    [MaxLength(4000, ErrorMessage = "Mô tả công việc tối đa 4000 ký tự.")]
    public string Description { get; set; } = "";   // Mô tả công việc (JD)

    [MaxLength(4000, ErrorMessage = "Yêu cầu ứng viên tối đa 4000 ký tự.")]
    public string Requirements { get; set; } = "";

    [MaxLength(120, ErrorMessage = "Địa điểm tối đa 120 ký tự.")]
    public string Location { get; set; } = "";

    [Range(0, 10_000, ErrorMessage = "Lương tối thiểu không hợp lệ.")]
    public decimal SalaryMin { get; set; }

    [Range(0, 10_000, ErrorMessage = "Lương tối đa không hợp lệ.")]
    public decimal SalaryMax { get; set; }

    public DateTime Deadline { get; set; }

    [Required, MaxLength(20)]
    public string Status { get; set; } = JobStatus.Open;

    // ===== ATS-04.1: 3 trường IT =====
    [Required, MaxLength(40, ErrorMessage = "Danh mục tối đa 40 ký tự.")]
    public string Category { get; set; } = "Khác";   // phải thuộc Job.Categories

    [MaxLength(400, ErrorMessage = "Tech Stack tối đa 400 ký tự.")]
    public string TechStack { get; set; } = "";      // "C#, .NET, SQL Server, Docker"

    [Required, MaxLength(20, ErrorMessage = "Cấp bậc tối đa 20 ký tự.")]
    public string Level { get; set; } = "Junior";    // phải thuộc Job.Levels

    public int CreatedById { get; set; }
    public User? CreatedBy { get; set; }

    /// <summary>
    /// P1-1: công ty đứng tên tin. Giá trị này LUÔN lấy từ công ty của người tạo, không bao
    /// giờ nhận từ form — form là thứ người gửi request sửa được, và sửa được nghĩa là đăng
    /// được tin đứng tên công ty khác.
    /// </summary>
    public int CompanyId { get; set; }
    public Company? Company { get; set; }

    public DateTime CreatedAt { get; set; }
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

    // ===== N2.C: Hình thức làm việc =====
    [Required, MaxLength(20)]
    public string EmploymentType { get; set; } = "Onsite";   // phải thuộc Job.EmploymentTypes

    public static readonly string[] EmploymentTypes = { "Onsite", "Remote", "Hybrid" };

    public static bool IsValidEmploymentType(string? e) => e is not null && EmploymentTypes.Contains(e);
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

    public DateTime Timestamp { get; set; }
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

    /// <summary>
    /// Số năm kinh nghiệm. Đây là nguồn DUY NHẤT để suy ra cấp bậc ứng viên (N1.F):
    /// trường Experience bên trên là văn bản tự do, không lọc theo cấp bậc được nếu
    /// không đoán mò nội dung người dùng gõ.
    /// </summary>
    [Range(0, 50, ErrorMessage = "Số năm kinh nghiệm phải từ 0 đến 50.")]
    public int YearsOfExperience { get; set; }

    // CV (PDF/DOCX, tối đa 5MB)
    public byte[]? CvData { get; set; }
    [MaxLength(260)] public string? CvFileName { get; set; }
    [MaxLength(120)] public string? CvContentType { get; set; }
    public DateTime? CvUploadedAt { get; set; }

    // ===== P2-3: đồng ý xử lý dữ liệu cá nhân bằng AI =====
    //
    // Nghị định 13/2023/NĐ-CP về bảo vệ dữ liệu cá nhân đòi sự đồng ý phải được THỂ HIỆN RÕ
    // và LƯU LẠI được. Trước bản này, việc gửi CV cho Gemini chỉ được nói bằng một dòng chữ
    // trên giao diện: không có bản ghi nào, không có cách nào từ chối, không có cách nào rút lại.

    /// <summary>Thời điểm đồng ý (UTC). Null nghĩa là CHƯA đồng ý hoặc đã rút lại.</summary>
    public DateTime? AiConsentAt { get; set; }

    /// <summary>
    /// Phiên bản điều khoản đã đồng ý. Khi nội dung điều khoản đổi, con số này cho biết
    /// người dùng đã đồng ý với BẢN NÀO — một chữ "đã đồng ý" trơ trọi không trả lời được
    /// câu hỏi đó, và đó chính là câu hỏi mà một lần kiểm tra sẽ đặt ra.
    /// </summary>
    [MaxLength(20)] public string? AiConsentVersion { get; set; }

    /// <summary>Đã đồng ý và chưa rút lại.</summary>
    public bool HasAiConsent => AiConsentAt.HasValue;

    /// <summary>Phiên bản điều khoản hiện hành — tăng khi nội dung điều khoản thay đổi.</summary>
    public const string CurrentAiConsentVersion = "2026-09-v1";

    /// <summary>
    /// P2-2: khóa của nội dung CV trong ICvStorage. Cột CvData cũ được GIỮ LẠI để đọc dữ
    /// liệu chưa di trú — xóa nó trong cùng một bản là cách chắc chắn nhất để mất CV của
    /// những hồ sơ mà lệnh di trú chưa chạy tới.
    /// </summary>
    [MaxLength(80)] public string? CvStorageKey { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ICollection<Application> Applications { get; set; } = new List<Application>();

    /// <summary>
    /// P2-2: "có CV" đúng khi nội dung nằm ở blob storage HOẶC còn trong cột cũ. Chỉ xét một
    /// trong hai thì sau khi di trú xong, mọi hồ sơ đột nhiên hiện "chưa có CV".
    /// </summary>
    public bool HasCv => CvStorageKey != null || (CvData != null && CvData.Length > 0);

    /// <summary>Cấp bậc suy ra từ số năm kinh nghiệm — chỉ để hiển thị, không lưu thành cột.</summary>
    public string Level => CandidateLevel.FromYears(YearsOfExperience);

    /// <summary>Tách chuỗi tags thành danh sách chip.</summary>
    public IReadOnlyList<string> SkillTagList => TechList.Parse(TechSkillTags);
}

/// <summary>
/// Cấp bậc ứng viên suy ra từ số năm kinh nghiệm (N1.F).
///
/// FromYears và YearRange phải luôn phát biểu CÙNG một bộ ngưỡng: bộ lọc so sánh
/// bằng YearRange ở trong SQL, còn nhãn hiển thị lấy từ FromYears. Hai bên lệch nhau
/// thì một ứng viên hiện nhãn "Senior" nhưng lại rơi vào kết quả lọc "Middle" — và
/// không có gì trên màn hình giải thích vì sao.
/// </summary>
public static class CandidateLevel
{
    public const string Fresher = "Fresher";   // chưa có kinh nghiệm đi làm
    public const string Junior = "Junior";     // 1 năm
    public const string Middle = "Middle";     // 2-4 năm
    public const string Senior = "Senior";     // từ 5 năm

    public static readonly string[] All = { Fresher, Junior, Middle, Senior };

    public static bool IsValid(string? level) => level is not null && All.Contains(level);

    public static string FromYears(int years) => years switch
    {
        <= 0 => Fresher,
        < 2 => Junior,
        < 5 => Middle,
        _ => Senior
    };

    /// <summary>Khoảng năm [Min, Max] của một cấp bậc, để lọc bằng SQL thay vì gọi FromYears từng dòng.</summary>
    public static (int Min, int Max) YearRange(string? level) => level switch
    {
        Fresher => (0, 0),
        Junior => (1, 1),
        Middle => (2, 4),
        Senior => (5, int.MaxValue),
        _ => (0, int.MaxValue)
    };
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
    /// <summary>P2-2: bản chụp có thể nằm ở blob storage hoặc còn trong cột cũ.</summary>
    public bool HasCvSnapshot => CvStorageKeySnapshot != null || (CvDataSnapshot != null && CvDataSnapshot.Length > 0);

    // Đã nộp | Đang xem xét | Phỏng vấn | Trúng tuyển | Từ chối
    [Required, MaxLength(30)]
    public string Status { get; set; } = ApplicationStatus.Submitted;

    public DateTime AppliedAt { get; set; }

    /// <summary>P2-2: khóa của BẢN CHỤP CV lúc nộp. Xem ghi chú ở CandidateProfile.CvStorageKey.</summary>
    [MaxLength(80)] public string? CvStorageKeySnapshot { get; set; }

    /// <summary>
    /// P1-4: phản hồi HIỆN CHO ỨNG VIÊN khi bị từ chối.
    ///
    /// Khác hẳn HrNote (lý do chốt điểm) và InternalNote (nhận định riêng của Mentor): đây
    /// là trường DUY NHẤT trong nhóm ghi chú được phép có mặt trong ApplicationDetail —
    /// record dùng chung với trang /my-applications/{id} của sinh viên. Trước đây sinh viên
    /// bị từ chối chỉ nhận đúng câu "đã chuyển sang trạng thái: Từ chối".
    /// </summary>
    [MaxLength(1000, ErrorMessage = "Phản hồi gửi ứng viên tối đa 1000 ký tự.")]
    public string? CandidateFeedback { get; set; }

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

    /// <summary>
    /// P1-3: số lần lịch phỏng vấn đã được đặt/đổi. Đi thẳng vào trường SEQUENCE của tệp
    /// .ics: cùng UID mà SEQUENCE không tăng thì ứng dụng lịch bỏ qua bản cập nhật và giữ
    /// nguyên giờ cũ — ứng viên đến vào giờ đã hủy.
    /// </summary>
    public int InterviewSequence { get; set; }

    /// <summary>
    /// P1-5: ai đã chốt điểm. Cả Admin lẫn Mentor đều chấm được, nên nếu không lưu lại thì
    /// trên màn hình "% chốt" là con số không có chủ — chỉ tra ngược được trong AuditLog.
    /// </summary>
    public int? HrScoreByUserId { get; set; }

    // ===== N1.B: ghi chú nội bộ của Mentor =====
    // Khác HrNote (lý do điều chỉnh điểm, gắn với ATS-16), đây là ghi chú tự do về ứng viên.
    // KHÔNG được đưa vào ApplicationDetail: record đó dùng chung cho cả trang Mentor lẫn
    // trang Sinh viên, nên một trường nằm ở đó chỉ cách chỗ rò đúng một dòng markup.
    [MaxLength(2000)] public string? InternalNote { get; set; }
    public int? InternalNoteByUserId { get; set; }
    public DateTime? InternalNoteAt { get; set; }

    // ===== N1.C: bộ câu hỏi phỏng vấn do AI sinh, lưu dạng JSON =====
    // Phải LƯU chứ không sinh lại mỗi lần mở trang: ứng dụng render tĩnh, luồng là
    // POST → redirect → GET, nên kết quả sinh ra trong POST mất sạch sau cú chuyển trang.
    // Sinh lại mỗi lần xem còn nghĩa là mỗi lần mở trang tốn một lượt gọi Gemini.
    [MaxLength(4000)] public string? AiQuestions { get; set; }
    [MaxLength(20)] public string? AiQuestionsSource { get; set; }
    public DateTime? AiQuestionsAt { get; set; }

    // ===== N1.E: lịch phỏng vấn gắn thẳng vào đơn (Hướng B — không tách trang riêng) =====
    public DateTime? InterviewAt { get; set; }
    [MaxLength(400)] public string? InterviewLink { get; set; }
    [MaxLength(500)] public string? InterviewNote { get; set; }

    // ATS-16.2: % dùng để xếp hạng = HrScore nếu có, ngược lại AiScore
    public int? FinalScore => HrScore ?? AiScore;
    public bool HasAiEvaluation => AiScore.HasValue;
    public bool IsOfflineEvaluation => AiSource == EvaluationSource.Offline;
    public bool HasInternalNote => !string.IsNullOrWhiteSpace(InternalNote);
    public bool HasAiQuestions => !string.IsNullOrWhiteSpace(AiQuestions);
    public bool HasInterviewSchedule => InterviewAt.HasValue;

    public ICollection<ApplicationStatusHistory> StatusHistory { get; set; } = new List<ApplicationStatusHistory>();
}

/// <summary>Nguồn sinh ra điểm phù hợp — quyết định nhãn hiển thị cho Mentor và Sinh viên.</summary>
public static class EvaluationSource
{
    public const string Gemini = "Gemini";
    public const string Offline = "Offline";
}

/// <summary>Các trạng thái xử lý hồ sơ (ATS-17 + P0-4).</summary>
public static class ApplicationStatus
{
    public const string Submitted = "Đã nộp";
    public const string Reviewing = "Đang xem xét";
    public const string Interview = "Phỏng vấn";
    public const string Accepted = "Trúng tuyển";
    public const string Rejected = "Từ chối";
    public const string Withdrawn = "Đã rút";

    /// <summary>Tất cả trạng thái (kể cả Đã rút) — dùng cho ô lọc và hiển thị.</summary>
    public static readonly string[] All = { Submitted, Reviewing, Interview, Accepted, Rejected, Withdrawn };

    /// <summary>Trạng thái Mentor được phép chọn trong dropdown — loại trừ "Đã rút".</summary>
    public static readonly string[] MentorSelectable = { Submitted, Reviewing, Interview, Accepted, Rejected };
}

/// <summary>
/// P1-4: luồng chuyển trạng thái hợp lệ.
///
/// Trước bản này UpdateStatus chỉ kiểm tra trạng thái mới có nằm trong danh sách hay không,
/// nên đi được "Trúng tuyển" → "Đã nộp" và "Từ chối" → "Phỏng vấn", và MỖI lần đổi lại bắn
/// một thông báo cho sinh viên. Không có trạng thái nào là kết thúc.
///
/// Bảng dưới đây được phát biểu ĐÚNG MỘT LẦN: dropdown của Mentor dựng từ NextStates, còn
/// UpdateStatus kiểm bằng CanTransition. Nếu tách thành hai bản, người dùng sẽ thấy một lựa
/// chọn rồi bị từ chối mà không có gì giải thích vì sao lựa chọn đó lại có ở đó.
/// </summary>
public static class ApplicationStatusFlow
{
    private static readonly Dictionary<string, string[]> Next = new()
    {
        [ApplicationStatus.Submitted] = new[] { ApplicationStatus.Reviewing, ApplicationStatus.Rejected },
        [ApplicationStatus.Reviewing] = new[] { ApplicationStatus.Interview, ApplicationStatus.Rejected },
        // "Phỏng vấn" → "Phỏng vấn" là hợp lệ và cố ý: đó là thao tác ĐỔI LỊCH hoặc hẹn vòng
        // tiếp theo. Bỏ nó đi thì Mentor không đổi được giờ hẹn sau khi đã gửi lời mời.
        [ApplicationStatus.Interview] = new[] { ApplicationStatus.Accepted, ApplicationStatus.Rejected, ApplicationStatus.Interview },
        // Ba trạng thái kết thúc: đơn đã chốt thì không quay lại pipeline được nữa.
        [ApplicationStatus.Accepted] = Array.Empty<string>(),
        [ApplicationStatus.Rejected] = Array.Empty<string>(),
        [ApplicationStatus.Withdrawn] = Array.Empty<string>()
    };

    public static bool CanTransition(string? from, string? to) =>
        from is not null && to is not null &&
        Next.TryGetValue(from, out var allowed) && allowed.Contains(to);

    /// <summary>Các bước tiếp theo hợp lệ; rỗng nghĩa là đơn đã chốt.</summary>
    public static IReadOnlyList<string> NextStates(string? from) =>
        from is not null && Next.TryGetValue(from, out var allowed) ? allowed : Array.Empty<string>();

    /// <summary>Đơn đã chốt — giao diện thay form đổi trạng thái bằng một dòng giải thích.</summary>
    public static bool IsTerminal(string? status) => NextStates(status).Count == 0;
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
    public DateTime ChangedAt { get; set; }
}

// ===== P1-3: Hàng đợi email =====

/// <summary>
/// Một email chờ gửi.
///
/// Vì sao có bảng này thay vì gửi thẳng trong request đổi trạng thái: SMTP chậm và hay lỗi.
/// Gửi đồng bộ thì một lần timeout làm nhà tuyển dụng thấy "đổi trạng thái thất bại" trong
/// khi trạng thái ĐÃ đổi — và không ai biết email có đi hay không.
///
/// Bản ghi được thêm TRONG CÙNG transaction với việc đổi trạng thái, nên đã đổi trạng thái
/// thì chắc chắn có email chờ gửi, và ngược lại giao dịch hỏng thì không sót email nào.
/// </summary>
public class EmailOutbox
{
    public int Id { get; set; }

    [Required, MaxLength(200)] public string ToEmail { get; set; } = "";
    [Required, MaxLength(300)] public string Subject { get; set; } = "";
    [Required, MaxLength(4000)] public string Body { get; set; } = "";

    [MaxLength(200)] public string? AttachmentName { get; set; }
    /// <summary>Nội dung tệp .ics — vài KB, không phải tệp người dùng tải lên.</summary>
    public byte[]? AttachmentContent { get; set; }

    public DateTime CreatedAt { get; set; }
    /// <summary>Null nghĩa là chưa gửi được — đó cũng là điều kiện quét của tiến trình nền.</summary>
    public DateTime? SentAt { get; set; }
    public int Attempts { get; set; }
    [MaxLength(500)] public string? LastError { get; set; }

    /// <summary>
    /// Quá số lần này thì bỏ hẳn. Không có trần, một địa chỉ email sai chính tả sẽ được thử
    /// lại 30 giây một lần cho tới khi ai đó để ý — tức là mãi mãi.
    /// </summary>
    public const int MaxAttempts = 5;
}

// ===== P1-2: Sinh viên tự kiểm tra độ phù hợp trước khi nộp =====

/// <summary>
/// Một lần sinh viên tự chạy đánh giá độ phù hợp với một tin bất kỳ.
///
/// Bảng RIÊNG, không ghi gì vào Application, vì hai lý do đối xứng nhau: điểm sinh viên tự
/// chạy không được lẫn vào con số nhà tuyển dụng đọc để sàng lọc, và ngược lại điểm nhà
/// tuyển dụng chấm không được đè lên kết quả sinh viên đang dùng để cải thiện hồ sơ.
///
/// Mỗi lần chạy ghi THÊM một bản ghi, không ghi đè: sinh viên cần thấy mình tiến bộ giữa
/// hai lần, và đó chính là giá trị hướng nghiệp mà ATS-14 hứa.
/// </summary>
public class SelfCheck
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public int JobId { get; set; }
    public Job? Job { get; set; }

    public int Score { get; set; }

    [MaxLength(1000)] public string? Strengths { get; set; }
    [MaxLength(1000)] public string? Missing { get; set; }
    [MaxLength(1000)] public string? Roadmap { get; set; }

    /// <summary>Gemini hay Offline — sinh viên phải biết mình đang đọc kết quả loại nào.</summary>
    [MaxLength(20)] public string? Source { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Số lượt tối đa mỗi người mỗi ngày. Mỗi lượt là một lần gọi Gemini và quota miễn phí
    /// có trần; không có hạn mức thì một người bấm liên tục là cả hệ thống mất tính năng này.
    /// </summary>
    public const int DailyLimit = 5;

    /// <summary>
    /// Giá trị Source của một lượt ĐÃ GIỮ CHỖ nhưng chưa có kết quả. Lượt được giữ chỗ (đếm
    /// vào hạn mức) TRƯỚC khi gọi AI, để năm request song song không cùng lọt qua bước đếm.
    /// Lượt đang chờ không bao giờ được hiện ra như một kết quả.
    /// </summary>
    public const string PendingSource = "Pending";
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
    public DateTime CreatedAt { get; set; }
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
