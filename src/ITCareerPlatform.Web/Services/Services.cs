using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ITCareerPlatform.Data;
using ITCareerPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace ITCareerPlatform.Services;

// =====================================================================
//  INTERFACES (khớp Class Diagram v2 — Components/endpoints phụ thuộc interface)
// =====================================================================

public interface IAuthService { User? Validate(string email, string password); }

public interface IUserService
{
    List<User> GetAll();
    List<Role> GetRoles();
    /// <summary>Đếm bằng COUNT(*) — trang chủ trước đây nạp cả bảng Users chỉ để lấy .Count.</summary>
    int CountAll();
    int CountByRole(int roleId);
    /// <summary>Tên của đúng những user được hỏi — thay cho việc nạp toàn bộ bảng Users.</summary>
    Dictionary<int, string> GetNames(IEnumerable<int> ids);
    User Create(string fullName, string email, string password, int roleId, int actorUserId);
    void ToggleLock(int id, int actorUserId);
    void ChangeRole(int id, int roleId, int actorUserId);
    bool Register(string fullName, string email, string password, out string error);   // EXT-01
    /// <summary>N1.A: người dùng tự đổi mật khẩu. Làm MỌI phiên đang mở của tài khoản hết hiệu lực.</summary>
    bool ChangePassword(int userId, string currentPassword, string newPassword, out string error);

    /// <summary>
    /// Tạo tài khoản quản trị đầu tiên, CHỈ khi hệ thống chưa có Admin nào.
    /// Trả về false kèm lý do; "đã có Admin rồi" cũng trả false nhưng không phải lỗi.
    /// </summary>
    bool TryCreateFirstAdmin(string fullName, string email, string password, out string error);
}

public interface IJobService
{
    List<Job> GetAll();
    List<Job> GetOpen();
    /// <summary>Tin do một người cụ thể đăng — lọc bằng WHERE, không nạp cả bảng rồi lọc ở C#.</summary>
    List<Job> GetByOwner(int ownerUserId);
    int CountAll();
    int CountOpen();
    Job? GetById(int id);
    Job Create(Job job);
    void Update(int id, Job input, int actorUserId);        // ATS-05
    void Close(int id, int actorUserId);                    // ATS-06
    void Reopen(int id, int actorUserId);
    /// <summary>Actor có quyền sửa/đóng/mở lại tin này không (Admin hoặc người tạo tin).</summary>
    bool CanModify(int jobId, int actorUserId);
    // ATS-07 + N2.C: lọc theo Category + TechStack + Level + Lương + Hình thức + Địa điểm
    List<Job> Filter(string? category, string? techStack, string? level, string sort,
        decimal? minSalary = null, string? employmentType = null, string? location = null);
}

public interface IProfileService
{
    CandidateProfile? GetByUserId(int userId);
    CandidateProfile Save(int userId, CandidateProfile input);            // ATS-08
    (bool ok, string? error) SaveCv(int userId, byte[] data, string fileName, string contentType); // ATS-09 + SEC-01
}

public interface IApplicationService
{
    bool Apply(int jobId, int candidateUserId, out string message);       // ATS-10
    List<ApplicantListItem> GetByJob(int jobId, string sort = "date");    // ATS-11 + ATS-15
    /// <summary>N1.F: cùng danh sách đó nhưng có lọc. filter = null nghĩa là không lọc gì.</summary>
    List<ApplicantListItem> GetByJob(int jobId, string sort, ApplicantFilter? filter);
    List<MyApplicationItem> GetByCandidate(int userId);
    /// <summary>Bản đầy đủ, có kèm byte[] CV — chỉ dùng cho tải CV và chấm AI.</summary>
    Application? GetById(int id);
    /// <summary>Bản chiếu để hiển thị: mọi trường trang chi tiết cần, KHÔNG kèm byte[] CV.</summary>
    ApplicationDetail? GetDetail(int id);
    /// <summary>Đếm theo đúng những tin đang hiển thị, thay vì gộp cả bảng Applications.</summary>
    Dictionary<int, int> CountForJobs(IReadOnlyCollection<int> jobIds);
    int CountAll();
    int CountByStatus(string status);
    /// <summary>Thống kê ATS-18 — tổng hợp bằng GROUP BY trong SQL, không kéo bản ghi về.</summary>
    Dictionary<string, int> CountGroupedByStatus();
    List<CategoryCount> CountGroupedByCategory();

    // ===== N1.G: thống kê cho dashboard Mentor =====
    // mentorUserId = null nghĩa là TOÀN hệ thống (Admin); có giá trị thì chỉ tính trên
    // những tin do chính Mentor đó tạo — cùng ranh giới mà CanAccess/CanModify đang giữ.
    MentorStats GetMentorStats(int? mentorUserId);
    Dictionary<string, int> CountGroupedByStatus(int? mentorUserId);
    List<CategoryCount> CountGroupedByCategory(int? mentorUserId);
    /// <summary>Những tin hút hồ sơ nhất — để Mentor biết nên đẩy hay đóng tin nào.</summary>
    List<JobApplicantCount> TopJobsByApplicants(int? mentorUserId, int take = 5);
    /// <summary>Số đơn mỗi ngày, ĐÃ đắp đủ cả những ngày không có đơn nào.</summary>
    List<DayCount> ApplicationsPerDay(int? mentorUserId, int days = 14);
    HashSet<int> AppliedJobIds(int candidateUserId);
    bool CanAccess(int appId, int actorUserId, bool isAdmin);
    void SaveAiEvaluation(int appId, AiEvaluation eval);                  // ATS-13/14
    void SaveHrScore(int appId, int hrScore, string note, int actorUserId); // ATS-16
    bool UpdateStatus(int appId, string newStatus, int actorUserId, out string message); // ATS-17
    /// <summary>
    /// N1.E: đổi trạng thái kèm lịch phỏng vấn. Bắt buộc có lịch khi chuyển sang "Phỏng vấn";
    /// gọi với trạng thái đang có + lịch mới nghĩa là ĐỔI lịch.
    /// </summary>
    bool UpdateStatus(int appId, string newStatus, InterviewSchedule? schedule, int actorUserId, out string message);
    List<ApplicationStatusHistory> GetStatusHistory(int appId);

    // ===== N1.B: ghi chú nội bộ của Mentor =====
    // Hỏi riêng chứ không gắn vào ApplicationDetail: record đó dùng chung cho cả trang
    // Mentor lẫn trang Sinh viên. Chỗ gọi phải đi qua CanAccess trước.
    InternalNoteView? GetInternalNote(int appId);
    void SaveInternalNote(int appId, string? note, int actorUserId);

    // ===== N1.C: bộ câu hỏi phỏng vấn do AI sinh =====
    // Cũng nằm ngoài ApplicationDetail, cùng lý do với ghi chú nội bộ: đưa trước bộ câu
    // hỏi cho ứng viên thì buổi phỏng vấn không còn đo được gì nữa.
    InterviewQuestionSet? GetAiQuestions(int appId);
    void SaveAiQuestions(int appId, InterviewQuestionSet set);

    // ===== N1.G (bản của Nhóm 2) — dùng cho Mentor Command Center ở trang chủ =====
    // Ba hàm này nhận (actorUserId, isAdmin) thay vì int? mentorUserId như nhóm record
    // MentorStats bên dưới. Hai bộ cùng trả lời một loại câu hỏi; xem ghi chú ở MentorStats.
    int CountRecentApplicants(int actorUserId, bool isAdmin, int withinHours);
    int CountUnreviewed(int actorUserId, bool isAdmin);
    List<MentorApplicantItem> TopUnreviewed(int actorUserId, bool isAdmin, int take);
}

/// <summary>Ghi nhật ký thao tác quan trọng (ATS-02) — trước đây chỉ đổi vai trò được ghi.</summary>
public interface IAuditService
{
    void Record(int actorUserId, string action, string table, string details);
    /// <summary>Đọc theo trang. Bảng này chỉ tăng, nên nạp toàn bộ là chi phí không có trần.</summary>
    AuditPage GetPage(int page, int pageSize);
}

public record AuditEntry(int Id, string ActorName, string Action, string TableName, string Details, DateTime Timestamp);

public record AuditPage(IReadOnlyList<AuditEntry> Items, int Total, int Page, int PageSize)
{
    public int TotalPages => Total == 0 ? 1 : (int)Math.Ceiling((double)Total / PageSize);
    public bool HasPrev => Page > 1;
    public bool HasNext => Page < TotalPages;
}

public record CategoryCount(string Category, int Count);

/// <summary>Một hồ sơ kèm thông tin tin tuyển dụng — cho bảng "Top hồ sơ chưa xem xét" (N2.I).</summary>
public record MentorApplicantItem(int Id, string FullName, string Email, string TechSkillTags,
    int? AiScore, int? HrScore, string Status, DateTime AppliedAt, int JobId, string JobTitle)
{
    public int? FinalScore => HrScore ?? AiScore;
}

// ===== N1.G: số liệu cho dashboard Mentor =====

/// <summary>
/// Tổng quan một lần gọi cho dashboard. Gom vào một record thay vì 10 hàm đếm rời:
/// cả 10 con số phải đến từ CÙNG một lát cắt dữ liệu, nếu không tổng các trạng thái
/// có thể lệch khỏi tổng số đơn ngay trên cùng một màn hình.
///
/// LƯU Ý SAU KHI GỘP NHÁNH: bộ này và ba hàm CountRecentApplicants/CountUnreviewed/
/// TopUnreviewed ở IApplicationService cùng trả lời "Mentor này đang có gì" nhưng khác
/// cách nhận phạm vi (int? mentorUserId so với actorUserId + isAdmin). Cả hai đang được
/// dùng: trang chủ gọi bộ sau, còn bộ này để dành cho /dashboard. Nên gộp về một bộ
/// trước khi thêm màn hình thống kê thứ ba.
/// </summary>
public record MentorStats(
    int TotalJobs,
    int OpenJobs,
    int TotalApplications,
    int PendingReview,          // "Đã nộp" — chưa ai xem
    int Reviewing,
    int Interviewing,
    int Accepted,
    int Rejected,
    int ApplicationsLast7Days,
    int ScoredApplications,     // số đơn đã có điểm (AI hoặc Mentor)
    int AvgFinalScore,          // trung bình % phù hợp của riêng những đơn đã chấm
    int ConversionRate)         // % trúng tuyển trên tổng đơn
{
    public bool HasApplications => TotalApplications > 0;

    /// <summary>Phân biệt "trung bình bằng 0" với "chưa chấm đơn nào" — giao diện hiển thị khác nhau.</summary>
    public bool HasScores => ScoredApplications > 0;
}

public record JobApplicantCount(int JobId, string JobTitle, string Category, string Level, int Count);

public record DayCount(DateTime Day, int Count);

public interface INotificationService                                     // NTF-01
{
    void Add(int userId, string title, string message, string link);
    List<Notification> GetForUser(int userId, int take = 20);
    int CountUnread(int userId);
    /// <summary>Chỉ đánh dấu thông báo THUỘC VỀ userId. Trả về Link đã lưu để chuyển trang.</summary>
    string? MarkRead(int id, int userId);
}

// ===== DTO nhẹ: chỉ các cột cần hiển thị, KHÔNG kèm byte[] CV =====
public record ApplicantListItem(int Id, string FullName, string Email, string TechSkillTags,
    int? AiScore, int? HrScore, string Status, DateTime AppliedAt,
    bool HasCv, int YearsOfExperience)
{
    public int? FinalScore => HrScore ?? AiScore;   // ATS-16.2
    public IReadOnlyList<string> SkillTagList => TechList.Parse(TechSkillTags);
    public string LevelName => CandidateLevel.FromYears(YearsOfExperience);
}

/// <summary>
/// N1.F: bốn khoảng % phù hợp. "Chưa đánh giá" là lựa chọn THỨ TƯ bắt buộc — chỉ có ba
/// khoảng số thì những đơn chưa ai chấm biến mất khỏi mọi lựa chọn, và không có gì trên
/// màn hình nói cho Mentor biết vì sao danh sách thiếu người.
/// </summary>
public static class ScoreBand
{
    public const string High = "> 80%";
    public const string Mid = "50 - 80%";
    public const string Low = "< 50%";

    // Nhan o tren chi la chu hien ra man hinh; con so that nam o ScoreThreshold và được
    // dùng cho cả badge lẫn truy vấn lọc. Hai dòng kiểm tra dưới đây chặn việc sửa một
    // bên mà quên bên kia — sai lệch sẽ lộ ra ngay ở lần chạy test đầu tiên.
    public static bool LabelsMatchThresholds =>
        High == $"> {ScoreThreshold.HighAbove}%"
        && Mid == $"{ScoreThreshold.MidFrom} - {ScoreThreshold.HighAbove}%"
        && Low == $"< {ScoreThreshold.MidFrom}%";
    public const string Unscored = "Chưa đánh giá";

    public static readonly string[] All = { High, Mid, Low, Unscored };
}

/// <summary>
/// N1.F: bộ lọc danh sách ứng viên. Mọi trường null nghĩa là "không lọc theo tiêu chí này",
/// nên một filter rỗng cho ra đúng kết quả như không lọc.
/// </summary>
public record ApplicantFilter(
    string? Status = null,
    bool? HasCv = null,
    string? Band = null,
    string? Level = null,
    IReadOnlyList<string>? RequiredTech = null)
{
    /// <summary>Có tiêu chí nào đang bật không — để giao diện biết lúc nào hiện nút "Xóa lọc".</summary>
    public bool IsActive =>
        !string.IsNullOrWhiteSpace(Status) || HasCv is not null ||
        !string.IsNullOrWhiteSpace(Band) || !string.IsNullOrWhiteSpace(Level) ||
        RequiredTech is { Count: > 0 };
}

public record MyApplicationItem(int Id, int JobId, string JobTitle, string Category, string Level,
    string CvFileNameSnapshot, int? AiScore, string? AiSource, string Status, DateTime AppliedAt,
    DateTime? InterviewAt, string? InterviewLink)
{
    /// <summary>Lịch phỏng vấn hiện lên ngay trên thẻ đơn ở /my-applications (N1.E — Hướng B).</summary>
    public bool HasInterview => InterviewAt.HasValue;
}

/// <summary>
/// N1.E: lịch phỏng vấn Mentor điền khi chuyển đơn sang trạng thái "Phỏng vấn".
/// Link để trống được chấp nhận (phỏng vấn trực tiếp); thời gian thì không.
/// </summary>
public record InterviewSchedule(DateTime At, string? Link, string? Note);

/// <summary>N1.B: ghi chú nội bộ kèm người ghi và thời điểm. Chỉ trả về cho Mentor/Admin.</summary>
public record InternalNoteView(string Note, int ByUserId, DateTime At);

/// <summary>Kết quả đánh giá đã lưu — dùng chung cho thẻ hiển thị của Mentor và Sinh viên.</summary>
public record AiResult(int? Score, string? Strengths, string? Missing, string? Roadmap, string? Source)
{
    public bool HasEvaluation => Score.HasValue;
    public bool IsOffline => Source == EvaluationSource.Offline;
}

/// <summary>Toàn bộ dữ liệu trang chi tiết đơn cần — không có byte[] nào.</summary>
public record ApplicationDetail(
    int Id, int JobId, string JobTitle, string JobCategory, string JobLevel, string JobTechStack,
    int JobCreatedById, string Status, DateTime AppliedAt,
    string CvFileNameSnapshot, bool HasCv,
    int? AiScore, string? AiStrengths, string? AiMissing, string? AiRoadmap, string? AiSource,
    int? HrScore, string? HrNote,
    int CandidateUserId, string FullName, string Email, string Phone, DateTime? DateOfBirth,
    string Address, string Education, string Experience, string Skills,
    string GithubUrl, string LinkedInUrl, string PortfolioUrl, string TechSkillTags,
    DateTime? InterviewAt, string? InterviewLink, string? InterviewNote, int YearsOfExperience)
{
    public int? FinalScore => HrScore ?? AiScore;
    public bool HasAiEvaluation => AiScore.HasValue;
    public IReadOnlyList<string> SkillTagList => TechList.Parse(TechSkillTags);
    public AiResult Ai => new(AiScore, AiStrengths, AiMissing, AiRoadmap, AiSource);

    public bool HasInterview => InterviewAt.HasValue;

    /// <summary>Cấp bậc suy ra từ số năm kinh nghiệm — cùng ngưỡng mà bộ lọc N1.F dùng.</summary>
    public string CandidateLevelName => CandidateLevel.FromYears(YearsOfExperience);
}

// =====================================================================
//  AuthService (mật khẩu băm BCrypt)
// =====================================================================
public class AuthService(AppDbContext db) : IAuthService
{
    /// <summary>
    /// Hash giả để luôn tốn đúng một lần BCrypt.Verify kể cả khi email không tồn tại.
    /// Bản cũ trả null ngay khi không tìm thấy email, nên thời gian phản hồi tiết lộ
    /// email nào có thật — đếm được bằng đồng hồ dù thông báo lỗi cố tình mơ hồ.
    /// </summary>
    private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword("khong-bao-gio-trung-khop");

    public User? Validate(string email, string password)
    {
        var normalized = UserService.NormalizeEmail(email);

        // So sánh trực tiếp trên cột: LOWER(Email) = LOWER(@p) khiến index duy nhất
        // trên Users.Email không dùng được, biến mỗi lần đăng nhập thành một lần quét bảng.
        var u = db.Users.Include(x => x.Role)
                        .FirstOrDefault(x => x.Email == normalized);

        var hash = u?.PasswordHash ?? DummyHash;
        var passwordOk = BCrypt.Net.BCrypt.Verify(password ?? "", hash);

        if (u is null || !u.IsActive || !passwordOk) return null;
        return u;
    }
}

// =====================================================================
//  UserService (ATS-01, ATS-02, EXT-01)
// =====================================================================
public class UserService(AppDbContext db, IAuditService? audit = null) : IUserService
{
    private const int MinPasswordLength = 8;

    /// <summary>Email lưu và tra cứu ở dạng chuẩn hóa, để so sánh bằng '=' vẫn đúng ở mọi collation.</summary>
    public static string NormalizeEmail(string? email) => (email ?? "").Trim().ToLowerInvariant();

    public List<User> GetAll() => db.Users.Include(u => u.Role).OrderBy(u => u.Id).ToList();

    public List<Role> GetRoles() => db.Roles.AsNoTracking().OrderBy(r => r.Id).ToList();

    public int CountAll() => db.Users.Count();

    public int CountByRole(int roleId) => db.Users.Count(u => u.RoleId == roleId);

    public Dictionary<int, string> GetNames(IEnumerable<int> ids)
    {
        var wanted = ids.Distinct().ToList();
        if (wanted.Count == 0) return new Dictionary<int, string>();
        return db.Users.AsNoTracking()
                       .Where(u => wanted.Contains(u.Id))
                       .Select(u => new { u.Id, u.FullName })
                       .ToDictionary(x => x.Id, x => x.FullName);
    }

    // ATS-01: Admin tạo tài khoản. Cùng bộ luật với đường tự đăng ký — trước đây
    // đường này không kiểm tra gì, nên email trùng thành lỗi 500 chưa bắt.
    public User Create(string fullName, string email, string password, int roleId, int actorUserId)
    {
        var normalized = NormalizeEmail(email);
        ValidateAccount(fullName, normalized, password);

        if (!db.Roles.Any(r => r.Id == roleId))
            throw new ArgumentException("Vai trò không hợp lệ.");
        if (db.Users.Any(u => u.Email == normalized))
            throw new ArgumentException("Email này đã được đăng ký, vui lòng dùng email khác.");

        var u = new User
        {
            FullName = fullName.Trim(),
            Email = normalized,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            RoleId = roleId,
            IsActive = true
        };
        db.Users.Add(u);
        db.SaveChanges();

        audit?.Record(actorUserId, "Create User", "Users", $"Tạo tài khoản '{u.FullName}' ({u.Email}).");
        return u;
    }

    public void ToggleLock(int id, int actorUserId = 0)
    {
        var u = db.Users.Find(id);
        if (u is null) return;
        u.IsActive = !u.IsActive;
        // Đổi SecurityStamp để cookie đang lưu hành của user này bị từ chối ở request kế tiếp.
        u.SecurityStamp++;
        db.SaveChanges();

        audit?.Record(actorUserId, u.IsActive ? "Unlock User" : "Lock User", "Users",
            $"{(u.IsActive ? "Mở khóa" : "Khóa")} tài khoản '{u.FullName}'.");
    }

    public void ChangeRole(int id, int roleId, int actorUserId)
    {
        var u = db.Users.Find(id);
        if (u is null) return;
        if (!db.Roles.Any(r => r.Id == roleId))
            throw new ArgumentException("Vai trò không hợp lệ.");

        var oldRole = db.Roles.Find(u.RoleId)?.RoleName ?? "?";
        var newRole = db.Roles.Find(roleId)?.RoleName ?? "?";
        u.RoleId = roleId;
        // Vai trò nằm trong cookie; đổi stamp để cookie cũ mang vai trò cũ bị loại ngay.
        u.SecurityStamp++;

        // ATS-02: ghi AuditLog ở tầng ứng dụng (minh bạch hơn Trigger)
        db.AuditLogs.Add(new AuditLog
        {
            UserId = actorUserId,
            Action = "Change Role",
            TableName = "Users",
            Details = $"Đổi vai trò '{u.FullName}': {oldRole} → {newRole}",
            Timestamp = DateTime.Now
        });
        db.SaveChanges();
    }

    // EXT-01: Sinh viên IT tự đăng ký (RoleId = 3)
    public bool Register(string fullName, string email, string password, out string error)
    {
        error = "";
        var normalized = NormalizeEmail(email);
        try
        {
            ValidateAccount(fullName, normalized, password);
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }

        if (db.Users.Any(u => u.Email == normalized))
        { error = "Email này đã được đăng ký, vui lòng dùng email khác hoặc đăng nhập."; return false; }

        db.Users.Add(new User
        {
            FullName = fullName.Trim(),
            Email = normalized,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            RoleId = Roles.StudentId,
            IsActive = true
        });
        db.SaveChanges();
        return true;
    }

    // N1.A: đổi mật khẩu. Trả false kèm lý do thay vì ném ngoại lệ — endpoint chỉ việc
    // đưa thông báo ngược về form, giống đường Register.
    public bool ChangePassword(int userId, string currentPassword, string newPassword, out string error)
    {
        error = "";
        var u = db.Users.Find(userId);

        // Không tách "không tìm thấy tài khoản" khỏi "sai mật khẩu": người gọi đã đăng nhập
        // rồi nên hai trường hợp chỉ khác nhau khi có ai đó đang dò id, và khi đó thông báo
        // khác nhau chính là thứ xác nhận id nào có thật.
        if (u is null || !BCrypt.Net.BCrypt.Verify(currentPassword ?? "", u.PasswordHash))
        { error = "Mật khẩu hiện tại không đúng."; return false; }

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < MinPasswordLength)
        { error = $"Mật khẩu mới phải có tối thiểu {MinPasswordLength} ký tự."; return false; }

        if (BCrypt.Net.BCrypt.Verify(newPassword, u.PasswordHash))
        { error = "Mật khẩu mới phải khác mật khẩu hiện tại."; return false; }

        u.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);

        // Đổi stamp để mọi phiên đang mở bằng mật khẩu CŨ bị từ chối ở request kế tiếp,
        // kể cả phiên trên máy khác — đó mới là điều người dùng mong đợi khi đổi mật khẩu.
        // Hệ quả: chính người vừa đổi cũng mất phiên, nên endpoint phải chủ động SignOut
        // và đưa họ về trang đăng nhập, thay vì để họ bị văng ra giữa chừng ở một request
        // bất kỳ sau đó mà không hiểu vì sao.
        u.SecurityStamp++;
        db.SaveChanges();

        audit?.Record(userId, "Change Password", "Users", $"Đổi mật khẩu tài khoản '{u.FullName}'.");
        return true;
    }

    // Đường mở khóa một bản triển khai mới. Dữ liệu mẫu chỉ nạp ở Development, nên một
    // CSDL thật vừa tạo xong không có Admin nào — mà /account/register chỉ sinh ra Sinh
    // viên và /users/create lại đòi sẵn quyền Admin. Không có hàm này thì bản triển khai
    // đầu tiên chạy được nhưng không ai quản trị nổi.
    public bool TryCreateFirstAdmin(string fullName, string email, string password, out string error)
    {
        error = "";

        // Điều kiện "chưa có Admin nào" là thứ giữ cho đường này không thành cửa hậu:
        // biến môi trường bị quên xóa sau lần triển khai đầu sẽ không thêm được quản trị
        // viên nào nữa, vì lúc đó hệ thống đã có Admin.
        if (db.Users.Any(u => u.RoleId == Roles.AdminId))
        { error = "Hệ thống đã có tài khoản quản trị — bỏ qua."; return false; }

        var normalized = NormalizeEmail(email);
        try
        {
            // Cùng bộ luật với hai đường tạo tài khoản kia, kể cả mức tối thiểu 8 ký tự:
            // tài khoản quyền cao nhất không có lý do gì được nới lỏng hơn tài khoản thường.
            ValidateAccount(fullName, normalized, password);
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }

        if (db.Users.Any(u => u.Email == normalized))
        { error = "Email này đã được dùng cho một tài khoản khác."; return false; }

        db.Users.Add(new User
        {
            FullName = fullName.Trim(),
            Email = normalized,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            RoleId = Roles.AdminId,
            IsActive = true
        });
        db.SaveChanges();
        return true;
    }

    /// <summary>Bộ luật dùng chung cho cả hai đường tạo tài khoản.</summary>
    private static void ValidateAccount(string fullName, string normalizedEmail, string password)
    {
        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(normalizedEmail) || string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Vui lòng nhập đầy đủ họ tên, email và mật khẩu.");
        if (password.Length < MinPasswordLength)
            throw new ArgumentException($"Mật khẩu phải có tối thiểu {MinPasswordLength} ký tự.");
        if (!new EmailAddressAttribute().IsValid(normalizedEmail))
            throw new ArgumentException("Email không đúng định dạng.");
    }
}

// =====================================================================
//  AuditService (ATS-02)
// =====================================================================
public class AuditService(AppDbContext db) : IAuditService
{
    public void Record(int actorUserId, string action, string table, string details)
    {
        // Không có người thực hiện xác định (job nền, seed) thì không ghi — FK UserId là bắt buộc.
        if (actorUserId <= 0) return;

        db.AuditLogs.Add(new AuditLog
        {
            UserId = actorUserId,
            Action = Truncate(action, 60),
            TableName = Truncate(table, 60),
            Details = Truncate(details, 500),
            Timestamp = DateTime.Now
        });
        db.SaveChanges();
    }

    public AuditPage GetPage(int page, int pageSize)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);

        var total = db.AuditLogs.Count();
        // Kẹp cả trần trên, không chỉ trần dưới: ?p=9999 trên 3 trang dữ liệu trước đây cho
        // ra một bảng rỗng kèm dòng "Trang 9999 / 3" và không có nút nào quay lại được.
        var lastPage = total == 0 ? 1 : (int)Math.Ceiling((double)total / pageSize);
        page = Math.Clamp(page, 1, lastPage);
        // Tên người thực hiện lấy kèm trong cùng truy vấn. Bản cũ nạp toàn bộ bảng Users
        // (kể cả PasswordHash) chỉ để dựng từ điển id -> tên.
        var items = db.AuditLogs.AsNoTracking()
            .OrderByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditEntry(
                a.Id,
                a.User != null ? a.User.FullName : "#" + a.UserId,
                a.Action, a.TableName, a.Details, a.Timestamp))
            .ToList();

        return new AuditPage(items, total, page, pageSize);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}

// =====================================================================
//  JobService (ATS-04, ATS-05, ATS-06, ATS-07)
// =====================================================================
public class JobService(AppDbContext db, IAuditService? audit = null) : IJobService
{
    public List<Job> GetAll() =>
        db.Jobs.OrderByDescending(j => j.CreatedAt).ThenByDescending(j => j.Id).ToList();

    public List<Job> GetOpen() =>
        db.Jobs.Where(j => j.Status == JobStatus.Open)
               .OrderByDescending(j => j.CreatedAt).ThenByDescending(j => j.Id).ToList();

    public List<Job> GetByOwner(int ownerUserId) =>
        db.Jobs.AsNoTracking().Where(j => j.CreatedById == ownerUserId)
               .OrderByDescending(j => j.CreatedAt).ThenByDescending(j => j.Id).ToList();

    public int CountAll() => db.Jobs.Count();

    public int CountOpen() => db.Jobs.Count(j => j.Status == JobStatus.Open);

    public Job? GetById(int id) => db.Jobs.Find(id);

    public Job Create(Job job)
    {
        Validate(job);
        job.Status = JobStatus.Open;
        db.Jobs.Add(job);
        db.SaveChanges();

        audit?.Record(job.CreatedById, "Create Job", "Jobs", $"Tạo tin '{job.Title}'.");
        return job;
    }

    // ATS-05: chỉ người tạo hoặc Admin mới sửa; không sửa tin Closed
    public void Update(int id, Job input, int actorUserId)
    {
        var j = RequireOwnership(id, actorUserId);
        if (j.Status == JobStatus.Closed)
            throw new InvalidOperationException("Tin đã đóng, vui lòng mở lại trước khi sửa.");

        // Bản cũ chép thẳng input vào entity mà không kiểm tra lại, nên một tin có thể
        // được SỬA thành trạng thái mà đường TẠO từ chối (tiêu đề rỗng, lương đảo ngược).
        Validate(input);

        j.Title = input.Title;
        j.Description = input.Description;
        j.Requirements = input.Requirements;
        j.Location = input.Location;
        j.SalaryMin = input.SalaryMin;
        j.SalaryMax = input.SalaryMax;
        j.Deadline = input.Deadline;
        j.Category = input.Category;
        j.TechStack = input.TechStack;
        j.Level = input.Level;
        // N2.C: hình thức làm việc cũng phải được chép sang. Thiếu dòng này thì form sửa
        // gửi lên đúng giá trị, Validate() kiểm tra đúng giá trị, rồi giá trị bị bỏ đi.
        j.EmploymentType = input.EmploymentType;
        db.SaveChanges();

        audit?.Record(actorUserId, "Update Job", "Jobs", $"Sửa tin #{j.Id} '{j.Title}'.");
    }

    // ATS-06: đóng/mở lại tin. Hai thao tác này trước đây chỉ kiểm tra VAI TRÒ chứ không
    // kiểm tra QUYỀN SỞ HỮU, nên bất kỳ Mentor nào cũng đóng được tin của Mentor khác —
    // và nút bấm hiện sẵn trên /jobs vì danh sách không lọc theo người tạo.
    public void Close(int id, int actorUserId) => SetStatus(id, actorUserId, JobStatus.Closed, "Đóng");

    public void Reopen(int id, int actorUserId) => SetStatus(id, actorUserId, JobStatus.Open, "Mở lại");

    private void SetStatus(int id, int actorUserId, string status, string label)
    {
        var j = RequireOwnership(id, actorUserId);
        if (j.Status == status) return;
        j.Status = status;
        db.SaveChanges();

        audit?.Record(actorUserId, label + " Job", "Jobs", $"{label} tin #{j.Id} '{j.Title}'.");
    }

    public bool CanModify(int jobId, int actorUserId)
    {
        var ownerId = db.Jobs.Where(j => j.Id == jobId).Select(j => (int?)j.CreatedById).FirstOrDefault();
        if (ownerId is null) return false;
        return ownerId == actorUserId || IsAdmin(actorUserId);
    }

    /// <summary>
    /// Một chỗ duy nhất phát biểu luật "Admin hoặc người tạo tin". Trước đây luật này được
    /// gõ lại ở 5-7 nơi và hai endpoint quan trọng nhất bị bỏ sót hoàn toàn.
    /// </summary>
    private Job RequireOwnership(int id, int actorUserId)
    {
        var j = db.Jobs.Find(id) ?? throw new InvalidOperationException("Không tìm thấy tin.");
        if (j.CreatedById != actorUserId && !IsAdmin(actorUserId))
            throw new UnauthorizedAccessException("Bạn không có quyền thao tác trên tin này.");
        return j;
    }

    private bool IsAdmin(int userId) =>
        db.Users.Where(u => u.Id == userId).Select(u => (int?)u.RoleId).FirstOrDefault() == Roles.AdminId;

    /// <summary>ATS-04.3: luật nghiệp vụ dùng chung cho cả tạo mới và cập nhật.</summary>
    private static void Validate(Job job)
    {
        // Ràng buộc khai báo trên entity (bắt buộc, độ dài, khoảng lương) kiểm tra lại ở
        // đây — giống ProfileService.Save. Thuộc tính maxlength trong form chỉ ràng buộc
        // trình duyệt; một request không qua trình duyệt với tiêu đề 500 ký tự trước đây
        // đi thẳng xuống SQL Server và nổ thành lỗi "string or binary data would be truncated".
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(job, new ValidationContext(job), results, validateAllProperties: true))
            throw new ArgumentException(results[0].ErrorMessage ?? "Dữ liệu tin tuyển dụng không hợp lệ.");

        if (string.IsNullOrWhiteSpace(job.Title))
            throw new ArgumentException("Tiêu đề công việc không được để trống.");
        if (job.SalaryMin < 0 || job.SalaryMax < 0)
            throw new ArgumentException("Lương không được âm.");
        if (job.SalaryMax > 0 && job.SalaryMin > 0 && job.SalaryMax < job.SalaryMin)
            throw new ArgumentException("Lương tối đa phải ≥ lương tối thiểu.");
        // Danh mục/cấp bậc trước đây chỉ được ràng buộc bởi thẻ <select>, nên một request
        // không qua trình duyệt có thể tạo danh mục tùy ý — tin đó biến mất khỏi bộ lọc
        // nhưng vẫn hiện thành một lát riêng trên biểu đồ Dashboard.
        if (!Job.IsValidCategory(job.Category))
            throw new ArgumentException("Danh mục công việc không hợp lệ.");
        if (!Job.IsValidLevel(job.Level))
            throw new ArgumentException("Cấp bậc không hợp lệ.");
        // N2.C: cùng lý do với Category/Level ở trên — thẻ <select> chỉ ràng buộc trình duyệt,
        // nên một request không qua trình duyệt vẫn đặt được hình thức làm việc tùy ý, và tin
        // đó sẽ biến mất khỏi bộ lọc "Onsite/Remote/Hybrid" mà không ai giải thích được.
        if (!Job.IsValidEmploymentType(job.EmploymentType))
            throw new ArgumentException("Hình thức làm việc không hợp lệ.");
    }

    // ATS-07 + N2.C: lọc + sắp xếp (chỉ tin Open — dành cho Sinh viên IT)
    public List<Job> Filter(string? category, string? techStack, string? level, string sort,
        decimal? minSalary = null, string? employmentType = null, string? location = null)
    {
        var today = DateTime.Today;
        // #9: chỉ hiện tin Open và CÒN hạn nộp
        var q = db.Jobs.AsNoTracking().Where(j => j.Status == JobStatus.Open && j.Deadline >= today);

        if (!string.IsNullOrWhiteSpace(category) && category != "Tất cả")
            q = q.Where(j => j.Category == category);

        if (!string.IsNullOrWhiteSpace(level) && level != "Tất cả")
            q = q.Where(j => j.Level == level);

        if (!string.IsNullOrWhiteSpace(techStack))
        {
            // Tìm chuỗi con nên không index được ở bất kỳ dạng nào; ToLower() giữ lại để
            // kết quả không phụ thuộc collation của máy chủ.
            var kw = techStack.Trim().ToLower();
            q = q.Where(j => j.TechStack.ToLower().Contains(kw));
        }

        if (minSalary.HasValue && minSalary.Value > 0)
            q = q.Where(j => j.SalaryMax >= minSalary.Value);

        if (!string.IsNullOrWhiteSpace(employmentType) && employmentType != "Tất cả")
            q = q.Where(j => j.EmploymentType == employmentType);

        if (!string.IsNullOrWhiteSpace(location))
        {
            var loc = location.Trim().ToLower();
            q = q.Where(j => j.Location.ToLower().Contains(loc));
        }

        q = sort switch
        {
            "deadline" => q.OrderBy(j => j.Deadline),
            _ => q.OrderByDescending(j => j.CreatedAt).ThenByDescending(j => j.Id)
        };
        return q.ToList();
    }
}

// =====================================================================
//  ProfileService (ATS-08, ATS-09, SEC-01)
// =====================================================================
public class ProfileService(AppDbContext db) : IProfileService
{
    public CandidateProfile? GetByUserId(int userId) =>
        db.CandidateProfiles.FirstOrDefault(p => p.UserId == userId);

    public CandidateProfile Save(int userId, CandidateProfile input)
    {
        // Ràng buộc khai báo trên entity (bắt buộc, độ dài, định dạng email/điện thoại)
        // được kiểm tra lại ở đây — thẻ required trong form chỉ ràng buộc trình duyệt.
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(input, new ValidationContext(input), results, validateAllProperties: true))
            throw new ArgumentException(results[0].ErrorMessage ?? "Dữ liệu hồ sơ không hợp lệ.");

        // Số điện thoại là tùy chọn; chỉ kiểm tra định dạng khi người dùng có nhập.
        if (!string.IsNullOrWhiteSpace(input.Phone) && !new PhoneAttribute().IsValid(input.Phone))
            throw new ArgumentException("Số điện thoại không hợp lệ.");

        // ATS-08.4: validate URL IT ở tầng server
        static void CheckUrl(string url, string prefix, string label)
        {
            if (!string.IsNullOrWhiteSpace(url) && !url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"{label} không hợp lệ (phải bắt đầu bằng {prefix}).");
        }
        CheckUrl(input.GithubUrl, "https://github.com/", "URL GitHub");
        CheckUrl(input.LinkedInUrl, "https://linkedin.com/", "URL LinkedIn");
        CheckUrl(input.PortfolioUrl, "https://", "URL Portfolio");

        var p = db.CandidateProfiles.FirstOrDefault(x => x.UserId == userId);
        if (p is null)
        {
            p = new CandidateProfile { UserId = userId };
            db.CandidateProfiles.Add(p);
        }
        p.FullName = input.FullName;
        p.Email = input.Email;
        p.Phone = input.Phone;
        p.DateOfBirth = input.DateOfBirth;
        p.Address = input.Address;
        p.Education = input.Education;
        p.Experience = input.Experience;
        p.YearsOfExperience = input.YearsOfExperience;
        p.Skills = input.Skills;
        p.GithubUrl = input.GithubUrl ?? "";
        p.LinkedInUrl = input.LinkedInUrl ?? "";
        p.PortfolioUrl = input.PortfolioUrl ?? "";
        p.TechSkillTags = input.TechSkillTags ?? "";
        db.SaveChanges();
        return p;
    }

    public (bool ok, string? error) SaveCv(int userId, byte[] data, string fileName, string contentType)
    {
        // SEC-01: quét tệp trước khi lưu
        var (safe, err) = CvScanner.Scan(data, fileName);
        if (!safe) return (false, err);

        var p = db.CandidateProfiles.FirstOrDefault(x => x.UserId == userId);
        if (p is null)
        {
            p = new CandidateProfile { UserId = userId };
            var u = db.Users.Find(userId);
            if (u != null) { p.FullName = u.FullName; p.Email = u.Email; }
            db.CandidateProfiles.Add(p);
        }
        p.CvData = data;
        p.CvFileName = fileName;
        // Content-type do trình duyệt gửi lên không đáng tin; suy ra từ nội dung thật đã quét.
        p.CvContentType = CvScanner.IsPdf(data)
            ? "application/pdf"
            : "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
        p.CvUploadedAt = DateTime.Now;
        db.SaveChanges();
        return (true, null);
    }
}

// =====================================================================
//  ApplicationService (ATS-10 → ATS-17)
// =====================================================================
public class ApplicationService(AppDbContext db, INotificationService notify, IAuditService? audit = null) : IApplicationService
{
    // ATS-10.2: 3 lớp kiểm tra nghiệp vụ
    public bool Apply(int jobId, int candidateUserId, out string message)
    {
        var job = db.Jobs.Find(jobId);
        if (job is null) { message = "Không tìm thấy tin tuyển dụng."; return false; }
        if (job.Status != JobStatus.Open) { message = "Vị trí tuyển dụng này đã đóng, không nhận hồ sơ."; return false; }
        // #9: chặn ứng tuyển khi đã quá hạn nộp (dù trạng thái vẫn Open)
        if (job.Deadline.Date < DateTime.Today) { message = "Tin tuyển dụng đã quá hạn nộp hồ sơ."; return false; }

        var profile = db.CandidateProfiles.FirstOrDefault(p => p.UserId == candidateUserId);
        if (profile is null) { message = "Bạn cần tạo hồ sơ IT trước khi ứng tuyển."; return false; }
        if (!profile.HasCv) { message = "Bạn cần tải CV lên trước khi ứng tuyển."; return false; }

        if (db.Applications.Any(a => a.JobId == jobId && a.CandidateProfileId == profile.Id))
        { message = "Bạn đã ứng tuyển vào vị trí này rồi."; return false; }

        db.Applications.Add(new Application
        {
            JobId = jobId,
            CandidateProfileId = profile.Id,
            // Đóng băng CV tại thời điểm nộp — về sau SV đổi CV cũng không ảnh hưởng đơn này
            CvFileNameSnapshot = profile.CvFileName ?? "(chưa tải CV)",
            CvDataSnapshot = profile.CvData,
            CvContentTypeSnapshot = profile.CvContentType,
            Status = ApplicationStatus.Submitted,
            AppliedAt = DateTime.Now
        });
        try
        {
            db.SaveChanges();
        }
        catch (DbUpdateException)
        {
            // #11: hai request cùng nộp một lúc → unique index (JobId, CandidateProfileId) chặn.
            message = "Bạn đã ứng tuyển vào vị trí này rồi.";
            return false;
        }
        message = "Ứng tuyển thành công!";
        return true;
    }

    // ATS-11 + ATS-15: danh sách ứng viên (projection — KHÔNG kéo byte[] CV về)
    public List<ApplicantListItem> GetByJob(int jobId, string sort = "date") => GetByJob(jobId, sort, null);

    // N1.F: cùng danh sách đó, thêm bộ lọc. HR làm việc theo pipeline chứ không theo từng
    // ứng viên lẻ, nên phần lớn thời gian họ muốn nhìn một lát cắt chứ không phải cả bảng.
    public List<ApplicantListItem> GetByJob(int jobId, string sort, ApplicantFilter? filter)
    {
        filter ??= new ApplicantFilter();
        var q = db.Applications.AsNoTracking().Where(a => a.JobId == jobId);

        if (!string.IsNullOrWhiteSpace(filter.Status))
            q = q.Where(a => a.Status == filter.Status);

        // "Có CV" phải nói cùng một điều mà nút Tải CV làm được: endpoint /applications/{id}/cv
        // phục vụ bản chụp, và nếu không có thì lùi về CV hiện tại của hồ sơ. Chỉ xét bản chụp
        // thì đơn cũ (chưa có cột snapshot) hiện "Thiếu CV" trong khi vẫn tải được CV.
        // So sánh cột blob với null dịch thành IS NOT NULL — nội dung CV KHÔNG bị kéo về.
        if (filter.HasCv == true)
            q = q.Where(a => a.CvDataSnapshot != null || a.CandidateProfile!.CvData != null);
        else if (filter.HasCv == false)
            q = q.Where(a => a.CvDataSnapshot == null && a.CandidateProfile!.CvData == null);

        // Lọc theo điểm chốt ngay trong SQL: (HrScore ?? AiScore) dịch thành COALESCE.
        // Không lọc trên FinalScore — đó là property tính ở C#, EF không dịch được, và
        // viết như vậy sẽ lặng lẽ kéo cả bảng về rồi mới lọc.
        // Đơn chưa chấm có COALESCE = NULL nên tự rơi khỏi cả ba khoảng số; đó là lý do
        // phải có khoảng "Chưa đánh giá" riêng.
        q = filter.Band switch
        {
            ScoreBand.High => q.Where(a => (a.HrScore ?? a.AiScore) > ScoreThreshold.HighAbove),
            ScoreBand.Mid => q.Where(a => (a.HrScore ?? a.AiScore) >= ScoreThreshold.MidFrom
                                       && (a.HrScore ?? a.AiScore) <= ScoreThreshold.HighAbove),
            ScoreBand.Low => q.Where(a => (a.HrScore ?? a.AiScore) < ScoreThreshold.MidFrom),
            ScoreBand.Unscored => q.Where(a => a.HrScore == null && a.AiScore == null),
            _ => q
        };

        if (CandidateLevel.IsValid(filter.Level))
        {
            var (min, max) = CandidateLevel.YearRange(filter.Level);
            q = q.Where(a => a.CandidateProfile!.YearsOfExperience >= min
                          && a.CandidateProfile.YearsOfExperience <= max);
        }

        var list = q.Select(a => new ApplicantListItem(
                a.Id, a.CandidateProfile!.FullName, a.CandidateProfile.Email,
                a.CandidateProfile.TechSkillTags, a.AiScore, a.HrScore, a.Status, a.AppliedAt,
                a.CvDataSnapshot != null || a.CandidateProfile.CvData != null,
                a.CandidateProfile.YearsOfExperience))
            .ToList();

        // Lọc tech làm SAU khi đã chiếu, ở phía C#. Chuẩn hóa của TechList (thường hóa,
        // gộp khoảng trắng, "SQL  Server" == "sql server") không viết được thành LIKE, nên
        // lọc trong SQL sẽ cho kết quả lệch với chính công thức mà phần chấm điểm dùng —
        // hai chỗ cùng nói về "ứng viên có Docker" mà trả lời khác nhau. Danh sách ở đây là
        // ứng viên của MỘT tin nên kích thước có trần.
        if (filter.RequiredTech is { Count: > 0 })
        {
            var wanted = filter.RequiredTech.Select(TechList.Normalize).ToHashSet();
            list = list.Where(x => wanted.IsSubsetOf(TechList.NormalizedSet(x.TechSkillTags))).ToList();
        }

        return sort == "score"
            ? list.OrderByDescending(x => x.FinalScore ?? -1).ThenByDescending(x => x.AppliedAt).ToList()
            : list.OrderByDescending(x => x.AppliedAt).ToList();
    }

    public List<MyApplicationItem> GetByCandidate(int userId) =>
        db.Applications.AsNoTracking()
            .Where(a => a.CandidateProfile!.UserId == userId)
            .OrderByDescending(a => a.AppliedAt)
            .Select(a => new MyApplicationItem(
                a.Id, a.JobId, a.Job!.Title, a.Job.Category, a.Job.Level,
                a.CvFileNameSnapshot, a.AiScore, a.AiSource, a.Status, a.AppliedAt,
                a.InterviewAt, a.InterviewLink))
            .ToList();

    // Bản đầy đủ (có byte[] CV) — chỉ dùng cho tải CV và chấm AI.
    public Application? GetById(int id) =>
        db.Applications.Include(a => a.Job)
                       .Include(a => a.CandidateProfile).ThenInclude(p => p!.User)
                       .FirstOrDefault(a => a.Id == id);

    // Bản chiếu cho hiển thị: bỏ hẳn hai cột byte[] (mỗi cột tới 5MB) khỏi đường truyền.
    public ApplicationDetail? GetDetail(int id) =>
        db.Applications.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new ApplicationDetail(
                a.Id, a.JobId, a.Job!.Title, a.Job.Category, a.Job.Level, a.Job.TechStack,
                a.Job.CreatedById, a.Status, a.AppliedAt,
                a.CvFileNameSnapshot, a.CvDataSnapshot != null || a.CandidateProfile!.CvData != null,
                a.AiScore, a.AiStrengths, a.AiMissing, a.AiRoadmap, a.AiSource,
                a.HrScore, a.HrNote,
                a.CandidateProfile!.UserId, a.CandidateProfile.FullName, a.CandidateProfile.Email,
                a.CandidateProfile.Phone, a.CandidateProfile.DateOfBirth, a.CandidateProfile.Address,
                a.CandidateProfile.Education, a.CandidateProfile.Experience, a.CandidateProfile.Skills,
                a.CandidateProfile.GithubUrl, a.CandidateProfile.LinkedInUrl,
                a.CandidateProfile.PortfolioUrl, a.CandidateProfile.TechSkillTags,
                a.InterviewAt, a.InterviewLink, a.InterviewNote,
                a.CandidateProfile.YearsOfExperience))
            .FirstOrDefault();

    public Dictionary<int, int> CountForJobs(IReadOnlyCollection<int> jobIds)
    {
        if (jobIds.Count == 0) return new Dictionary<int, int>();
        var ids = jobIds.ToList();
        return db.Applications.Where(a => ids.Contains(a.JobId))
                              .GroupBy(a => a.JobId)
                              .Select(g => new { JobId = g.Key, Count = g.Count() })
                              .ToDictionary(x => x.JobId, x => x.Count);
    }

    public int CountAll() => db.Applications.Count();

    public int CountByStatus(string status) => db.Applications.Count(a => a.Status == status);

    /// <summary>
    /// GROUP BY thật trong SQL. Bản cũ kết thúc truy vấn ở .GroupBy(...) rồi gọi
    /// Enumerable.ToDictionary, nên EF phải nạp TOÀN BỘ entity Application — kể cả cột
    /// CvDataSnapshot tới 5MB mỗi bản ghi — chỉ để đếm ra 5 con số.
    /// </summary>
    public Dictionary<string, int> CountGroupedByStatus() => CountGroupedByStatus(null);

    // Phần gộp phải chiếu vào anonymous type: EF không dịch được GroupBy khi Select dựng
    // thẳng một kiểu record tự định nghĩa. Sắp xếp và ánh xạ làm sau khi đã có kết quả —
    // chỉ vài dòng (mỗi chuyên ngành một dòng), nên không phải chi phí đáng kể.
    public List<CategoryCount> CountGroupedByCategory() => CountGroupedByCategory(null);

    // =====================================================================
    //  N1.G: thống kê cho dashboard Mentor.
    //
    //  Mọi truy vấn dưới đây đi qua ScopedApplications/ScopedJobs, nên ranh giới
    //  "chỉ tin của tôi" được phát biểu đúng MỘT lần. Dashboard cũ đếm trên toàn bảng,
    //  nghĩa là một Mentor đọc được lưu lượng tuyển dụng của mọi Mentor khác — trong khi
    //  chính người đó không mở nổi một đơn lẻ nào của họ, vì CanAccess đã chặn.
    // =====================================================================

    private IQueryable<Application> ScopedApplications(int? mentorUserId)
    {
        var q = db.Applications.AsNoTracking();
        return mentorUserId is null ? q : q.Where(a => a.Job!.CreatedById == mentorUserId);
    }

    private IQueryable<Job> ScopedJobs(int? mentorUserId)
    {
        var q = db.Jobs.AsNoTracking();
        return mentorUserId is null ? q : q.Where(j => j.CreatedById == mentorUserId);
    }

    public MentorStats GetMentorStats(int? mentorUserId)
    {
        var jobs = ScopedJobs(mentorUserId);
        var totalJobs = jobs.Count();
        var openJobs = jobs.Count(j => j.Status == JobStatus.Open);

        var byStatus = CountGroupedByStatus(mentorUserId);
        var total = byStatus.Values.Sum();
        var accepted = byStatus.GetValueOrDefault(ApplicationStatus.Accepted);

        // Mốc 7 ngày tính từ ĐẦU NGÀY chứ không từ thời điểm gọi hàm: nếu trừ thẳng
        // DateTime.Now, cùng một dashboard mở lúc 9h và lúc 17h sẽ ra hai con số khác nhau
        // mà không có gì trên màn hình giải thích vì sao.
        var since = DateTime.Today.AddDays(-6);
        var last7 = ScopedApplications(mentorUserId).Count(a => a.AppliedAt >= since);

        // Trung bình chỉ tính trên đơn ĐÃ chấm. Nếu gộp cả đơn chưa chấm vào mẫu số thì
        // mỗi đơn mới nộp lại kéo trung bình tụt xuống — trông như chất lượng ứng viên
        // đang giảm, trong khi thực ra chỉ là Mentor chưa bấm chấm.
        var scored = ScopedApplications(mentorUserId).Where(a => a.HrScore != null || a.AiScore != null);
        var scoredCount = scored.Count();
        // AVG chạy trong SQL; chiếu sang double? để EF sinh AVG(CAST(... AS float)) thay vì
        // kéo từng dòng về rồi mới cộng ở phía ứng dụng.
        var avgRaw = scored.Select(a => (double?)(a.HrScore ?? a.AiScore)).Average();

        return new MentorStats(
            TotalJobs: totalJobs,
            OpenJobs: openJobs,
            TotalApplications: total,
            PendingReview: byStatus.GetValueOrDefault(ApplicationStatus.Submitted),
            Reviewing: byStatus.GetValueOrDefault(ApplicationStatus.Reviewing),
            Interviewing: byStatus.GetValueOrDefault(ApplicationStatus.Interview),
            Accepted: accepted,
            Rejected: byStatus.GetValueOrDefault(ApplicationStatus.Rejected),
            ApplicationsLast7Days: last7,
            ScoredApplications: scoredCount,
            AvgFinalScore: avgRaw is null ? 0 : (int)Math.Round(avgRaw.Value),
            ConversionRate: total == 0 ? 0 : (int)Math.Round(100.0 * accepted / total));
    }

    public Dictionary<string, int> CountGroupedByStatus(int? mentorUserId) =>
        ScopedApplications(mentorUserId)
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionary(x => x.Status, x => x.Count);

    public List<CategoryCount> CountGroupedByCategory(int? mentorUserId) =>
        ScopedApplications(mentorUserId)
            .GroupBy(a => a.Job!.Category)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .ToList()
            .OrderByDescending(x => x.Count)
            .Select(x => new CategoryCount(x.Category, x.Count))
            .ToList();

    public List<JobApplicantCount> TopJobsByApplicants(int? mentorUserId, int take = 5)
    {
        take = Math.Clamp(take, 1, 50);
        return ScopedJobs(mentorUserId)
            .Select(j => new { j.Id, j.Title, j.Category, j.Level, Count = j.Applications.Count })
            .OrderByDescending(x => x.Count).ThenByDescending(x => x.Id)
            .Take(take)
            .ToList()
            .Select(x => new JobApplicantCount(x.Id, x.Title, x.Category, x.Level, x.Count))
            .ToList();
    }

    public List<DayCount> ApplicationsPerDay(int? mentorUserId, int days = 14)
    {
        days = Math.Clamp(days, 1, 90);
        var from = DateTime.Today.AddDays(-(days - 1));

        // Gộp theo ngày trong SQL, rồi ĐẮP ĐỦ những ngày không có đơn nào ở phía C#.
        // Thiếu bước đắp, biểu đồ nối thẳng qua ngày trống và trông như hồ sơ về đều đặn,
        // trong khi thực tế có những ngày không ai nộp.
        var raw = ScopedApplications(mentorUserId)
            .Where(a => a.AppliedAt >= from)
            .GroupBy(a => a.AppliedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToList()
            .ToDictionary(x => x.Day, x => x.Count);

        return Enumerable.Range(0, days)
            .Select(i => from.AddDays(i))
            .Select(d => new DayCount(d, raw.GetValueOrDefault(d)))
            .ToList();
    }

    /// <summary>Chỉ lấy cột JobId. Bản cũ dựng cả DTO có JOIN sang Jobs rồi vứt hết đi.</summary>
    public HashSet<int> AppliedJobIds(int candidateUserId) =>
        db.Applications.AsNoTracking()
                       .Where(a => a.CandidateProfile!.UserId == candidateUserId)
                       .Select(a => a.JobId)
                       .ToHashSet();

    // #5: Mentor chỉ được xem/thao tác đơn thuộc tin do mình tạo; Admin xem tất cả
    public bool CanAccess(int appId, int actorUserId, bool isAdmin)
    {
        if (isAdmin) return true;
        var ownerId = db.Applications.Where(a => a.Id == appId)
                                     .Select(a => (int?)a.Job!.CreatedById)
                                     .FirstOrDefault();
        return ownerId is not null && ownerId == actorUserId;
    }

    // ATS-13/14: lưu kết quả đánh giá — không đụng HrScore
    public void SaveAiEvaluation(int appId, AiEvaluation eval)
    {
        var a = db.Applications.Find(appId);
        if (a is null) return;
        a.AiScore = Math.Clamp(eval.MatchPercent, 0, 100);
        // Cắt đúng giới hạn cột (nvarchar(1000)). Ba trường này đến từ một mô hình ngoài:
        // không có gì buộc Gemini trả về dưới 1000 ký tự, và trên SQL Server thì vượt cột
        // là một lần ghi HỎNG — đơn mất luôn kết quả vừa chấm — chứ không phải chuỗi bị cắt.
        a.AiStrengths = Clip(eval.Strengths, 1000);
        a.AiMissing = Clip(eval.Missing, 1000);
        a.AiRoadmap = Clip(eval.Roadmap, 1000);
        a.AiSource = Clip(eval.Source, 20);      // Gemini hay Offline — hiển thị cho Mentor biết
        a.AiScoredAt = DateTime.Now;
        db.SaveChanges();
    }

    // ATS-16: Mentor điều chỉnh điểm (không đụng tới AiScore)
    public void SaveHrScore(int appId, int hrScore, string note, int actorUserId = 0)
    {
        var a = db.Applications.Find(appId);
        if (a is null) return;
        a.HrScore = Math.Clamp(hrScore, 0, 100);
        a.HrNote = note;
        a.HrAdjustedAt = DateTime.Now;
        db.SaveChanges();

        audit?.Record(actorUserId, "Score Applicant", "Applications",
            $"Chốt {a.HrScore}% cho đơn #{appId}: {note}");
    }

    // ATS-17: đổi trạng thái + ghi lịch sử + thông báo cho SV (cùng transaction)
    public bool UpdateStatus(int appId, string newStatus, int actorUserId, out string message) =>
        UpdateStatus(appId, newStatus, null, actorUserId, out message);

    // N1.E: lịch phỏng vấn ghi trong CHÍNH transaction đổi trạng thái. Tách thành hai thao
    // tác thì có khoảng thời gian đơn đã mang trạng thái "Phỏng vấn" nhưng chưa có giờ hẹn,
    // và sinh viên nhận được một lời mời không nói giờ nào.
    public bool UpdateStatus(int appId, string newStatus, InterviewSchedule? schedule, int actorUserId, out string message)
    {
        if (!ApplicationStatus.All.Contains(newStatus))
        { message = "Trạng thái không hợp lệ."; return false; }

        var a = db.Applications.Include(x => x.Job)
                               .Include(x => x.CandidateProfile)
                               .FirstOrDefault(x => x.Id == appId);
        if (a is null) { message = "Không tìm thấy đơn."; return false; }

        var statusChanged = a.Status != newStatus;

        // Giữ nguyên trạng thái mà không kèm lịch mới thì không có gì để làm. Còn giữ nguyên
        // trạng thái KÈM lịch mới chính là thao tác đổi lịch, phải chạy tiếp.
        if (!statusChanged && schedule is null)
        { message = "Trạng thái không thay đổi."; return false; }

        if (newStatus == ApplicationStatus.Interview)
        {
            if (schedule is null)
            { message = "Vui lòng nhập thời gian phỏng vấn khi chuyển sang trạng thái này."; return false; }

            var invalid = ValidateSchedule(schedule);
            if (invalid is not null) { message = invalid; return false; }
        }

        using var tx = db.Database.BeginTransaction();
        try
        {
            var from = a.Status;
            if (statusChanged)
            {
                db.ApplicationStatusHistories.Add(new ApplicationStatusHistory
                {
                    ApplicationId = a.Id,
                    FromStatus = from,
                    ToStatus = newStatus,
                    ChangedByUserId = actorUserId,
                    ChangedAt = DateTime.Now
                });
                a.Status = newStatus;
            }

            // Chuyển sang trạng thái khác KHÔNG xóa lịch cũ: Mentor và sinh viên vẫn cần
            // tra lại buổi phỏng vấn đã diễn ra khi đọc một đơn đã trúng tuyển hoặc bị từ chối.
            if (newStatus == ApplicationStatus.Interview && schedule is not null)
            {
                a.InterviewAt = schedule.At;
                a.InterviewLink = Clip(schedule.Link, 400);
                a.InterviewNote = Clip(schedule.Note, 500);
            }

            db.SaveChanges();

            // NTF-01: báo cho Sinh viên IT. Đường dẫn trỏ thẳng vào ĐƠN cụ thể thay vì danh
            // sách — với lời mời phỏng vấn, thứ cần đọc (giờ hẹn, link họp) nằm trong đơn đó.
            if (a.CandidateProfile != null)
                notify.Add(a.CandidateProfile.UserId,
                    statusChanged ? NotificationTitle(newStatus) : "Cập nhật lịch phỏng vấn",
                    BuildStatusMessage(a, newStatus, statusChanged),
                    $"/my-applications/{a.Id}");

            audit?.Record(actorUserId,
                statusChanged ? "Change Status" : "Reschedule Interview", "Applications",
                statusChanged ? $"Đơn #{appId}: {from} → {newStatus}" : $"Đơn #{appId}: đổi lịch phỏng vấn");

            tx.Commit();
            message = statusChanged ? "Đã cập nhật trạng thái." : "Đã cập nhật lịch phỏng vấn.";
            return true;
        }
        catch
        {
            tx.Rollback();
            message = "Có lỗi khi cập nhật trạng thái.";
            return false;
        }
    }

    private static string NotificationTitle(string status) =>
        status == ApplicationStatus.Interview ? "Mời phỏng vấn" : "Cập nhật đơn ứng tuyển";

    private static string BuildStatusMessage(Application a, string newStatus, bool statusChanged)
    {
        var title = a.Job?.Title ?? "vị trí đã ứng tuyển";
        if (newStatus != ApplicationStatus.Interview)
            return $"Đơn ứng tuyển vào '{title}' đã chuyển sang trạng thái: {newStatus}.";

        var opening = statusChanged
            ? $"Bạn được mời phỏng vấn vị trí '{title}'."
            : $"Lịch phỏng vấn vị trí '{title}' đã được cập nhật.";
        var link = string.IsNullOrWhiteSpace(a.InterviewLink) ? "" : $" Link: {a.InterviewLink}";
        return $"{opening} Thời gian: {Ui.DateTimeText(a.InterviewAt)}.{link}";
    }

    /// <summary>Trả null nếu lịch hợp lệ, ngược lại là lý do để hiện cho Mentor.</summary>
    internal static string? ValidateSchedule(InterviewSchedule s)
    {
        if (s.At == default) return "Vui lòng chọn thời gian phỏng vấn.";
        if (s.At <= DateTime.Now) return "Thời gian phỏng vấn phải ở tương lai.";
        if (!string.IsNullOrWhiteSpace(s.Link) && !IsSafeMeetingLink(s.Link))
            return "Link phỏng vấn phải là địa chỉ http hoặc https hợp lệ (Google Meet, Zoom, Teams...).";
        return null;
    }

    /// <summary>
    /// Chỉ chấp nhận http/https. Link này được render thành thẻ &lt;a href&gt; trên trang của
    /// sinh viên, nên một giá trị "javascript:..." lọt qua đây không phải là link hỏng —
    /// đó là một lỗ XSS do chính nhà tuyển dụng nhập vào.
    /// </summary>
    private static bool IsSafeMeetingLink(string link) =>
        Uri.TryCreate(link.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>Cắt đúng giới hạn cột; chuỗi rỗng lưu thành null để phân biệt "không có" với "có mà trống".</summary>
    private static string? Clip(string? value, int max)
    {
        var s = value?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        return s.Length > max ? s[..max] : s;
    }

    // ===== N1.C: bộ câu hỏi phỏng vấn =====
    // Lưu JSON trong một cột chứ không tách bảng riêng: bộ câu hỏi luôn được đọc và ghi
    // trọn gói theo đơn, không bao giờ truy vấn hay sắp xếp theo từng câu.
    private const int QuestionsColumnLimit = 4000;

    public void SaveAiQuestions(int appId, InterviewQuestionSet set)
    {
        var a = db.Applications.Find(appId);
        if (a is null || !set.HasQuestions) return;

        // Bỏ bớt câu cuối cho tới khi vừa cột, thay vì để chuỗi JSON bị cắt ngang —
        // chuỗi cắt ngang đọc lại sẽ ném JsonException chứ không hỏng một cách im lặng.
        var items = set.Items.ToList();
        var json = JsonSerializer.Serialize(items);
        while (json.Length > QuestionsColumnLimit && items.Count > 1)
        {
            items.RemoveAt(items.Count - 1);
            json = JsonSerializer.Serialize(items);
        }
        if (json.Length > QuestionsColumnLimit) return;   // một câu mà vẫn quá dài: không lưu còn hơn lưu hỏng

        a.AiQuestions = json;
        a.AiQuestionsSource = set.Source;
        a.AiQuestionsAt = DateTime.Now;
        db.SaveChanges();
    }

    public InterviewQuestionSet? GetAiQuestions(int appId)
    {
        var row = db.Applications.AsNoTracking()
            .Where(a => a.Id == appId && a.AiQuestions != null)
            .Select(a => new { a.AiQuestions, a.AiQuestionsSource })
            .FirstOrDefault();
        if (row?.AiQuestions is null) return null;

        try
        {
            var items = JsonSerializer.Deserialize<List<InterviewQuestion>>(row.AiQuestions);
            return items is null || items.Count == 0
                ? null
                : new InterviewQuestionSet(items, row.AiQuestionsSource ?? EvaluationSource.Offline);
        }
        catch (JsonException)
        {
            // Dữ liệu cũ hoặc bị sửa tay trong CSDL: coi như chưa có để Mentor sinh lại,
            // thay vì để cả trang chi tiết ứng viên đổ lỗi 500 vì một cột hỏng.
            return null;
        }
    }

    // ===== N1.B: ghi chú nội bộ của Mentor =====

    public InternalNoteView? GetInternalNote(int appId) =>
        db.Applications.AsNoTracking()
            .Where(a => a.Id == appId && a.InternalNote != null && a.InternalNote != "")
            .Select(a => new InternalNoteView(
                a.InternalNote!, a.InternalNoteByUserId ?? 0, a.InternalNoteAt ?? a.AppliedAt))
            .FirstOrDefault();

    public void SaveInternalNote(int appId, string? note, int actorUserId)
    {
        var a = db.Applications.Find(appId);
        if (a is null) return;

        var text = Clip(note, 2000);
        a.InternalNote = text;
        a.InternalNoteByUserId = text is null ? null : actorUserId;
        a.InternalNoteAt = text is null ? null : DateTime.Now;
        db.SaveChanges();

        // NỘI DUNG ghi chú không đi vào nhật ký: nhật ký hệ thống thì Admin đọc được, còn
        // ghi chú là nhận định riêng của Mentor về ứng viên. Chỉ ghi lại việc đã có thao tác.
        audit?.Record(actorUserId, text is null ? "Clear Internal Note" : "Save Internal Note",
            "Applications", $"Ghi chú nội bộ đơn #{appId}.");
    }

    public List<ApplicationStatusHistory> GetStatusHistory(int appId) =>
        db.ApplicationStatusHistories.AsNoTracking()
                                     .Where(h => h.ApplicationId == appId)
                                     .OrderBy(h => h.ChangedAt).ToList();

    // N1.G: Thống kê số hồ sơ ứng tuyển mới nộp trong withinHours gần nhất
    public int CountRecentApplicants(int actorUserId, bool isAdmin, int withinHours)
    {
        if (withinHours <= 0)
            throw new ArgumentException("Số giờ phải lớn hơn 0.", nameof(withinHours));

        var now = DateTime.Now;
        var cutoff = now.AddHours(-withinHours);

        var q = db.Applications.Where(a => a.AppliedAt >= cutoff);
        if (!isAdmin)
            q = q.Where(a => a.Job!.CreatedById == actorUserId);

        return q.Count();
    }

    // N1.G: Thống kê số hồ sơ chưa review (Status == ApplicationStatus.Submitted)
    public int CountUnreviewed(int actorUserId, bool isAdmin)
    {
        var q = db.Applications.Where(a => a.Status == ApplicationStatus.Submitted);
        if (!isAdmin)
            q = q.Where(a => a.Job!.CreatedById == actorUserId);

        return q.Count();
    }

    // N1.G: Lấy danh sách top hồ sơ chưa review mới nhất kèm Job context
    public List<MentorApplicantItem> TopUnreviewed(int actorUserId, bool isAdmin, int take)
    {
        if (take <= 0) return new List<MentorApplicantItem>();
        take = Math.Min(take, 50);

        var q = db.Applications.Where(a => a.Status == ApplicationStatus.Submitted);
        if (!isAdmin)
            q = q.Where(a => a.Job!.CreatedById == actorUserId);

        return q.OrderByDescending(a => a.AppliedAt)
                .Take(take)
                .Select(a => new MentorApplicantItem(
                    a.Id,
                    a.CandidateProfile!.FullName,
                    a.CandidateProfile.Email,
                    a.CandidateProfile.TechSkillTags,
                    a.AiScore,
                    a.HrScore,
                    a.Status,
                    a.AppliedAt,
                    a.JobId,
                    a.Job!.Title))
                .ToList();
    }
}

// =====================================================================
//  NotificationService (NTF-01)
// =====================================================================
public class NotificationService(AppDbContext db) : INotificationService
{
    public void Add(int userId, string title, string message, string link)
    {
        db.Notifications.Add(new Notification
        {
            UserId = userId,
            // Cắt đúng giới hạn cột. Từ N1.E, nội dung thông báo có thể mang tên tin (tối đa
            // 160 ký tự) kèm link họp (tối đa 400) — vượt 500 mà không cần ai cố ý, và trên
            // SQL Server thì đó là một lần ghi hỏng chứ không phải một chuỗi bị cắt.
            Title = Cut(title, 160),
            Message = Cut(message, 500),
            Link = Cut(link, 250),
            IsRead = false, CreatedAt = DateTime.Now
        });
        db.SaveChanges();
    }

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..max];

    public List<Notification> GetForUser(int userId, int take = 20) =>
        db.Notifications.AsNoTracking()
                        .Where(n => n.UserId == userId)
                        .OrderByDescending(n => n.CreatedAt).Take(take).ToList();

    public int CountUnread(int userId) =>
        db.Notifications.Count(n => n.UserId == userId && !n.IsRead);

    /// <summary>
    /// Chỉ đánh dấu thông báo thuộc về chính người gọi. Bản cũ nhận mỗi id, nên bất kỳ
    /// tài khoản nào cũng xóa được huy hiệu chưa đọc của sinh viên khác — và thông báo
    /// là kênh duy nhất báo tin đơn đã đổi trạng thái.
    /// Trả về Link ĐÃ LƯU trong CSDL; đường dẫn không bao giờ lấy từ dữ liệu người gửi.
    /// </summary>
    public string? MarkRead(int id, int userId)
    {
        var n = db.Notifications.FirstOrDefault(x => x.Id == id && x.UserId == userId);
        if (n is null) return null;
        if (!n.IsRead)
        {
            n.IsRead = true;
            db.SaveChanges();
        }
        return n.Link;
    }
}
