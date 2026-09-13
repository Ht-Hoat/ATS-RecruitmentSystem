using System.ComponentModel.DataAnnotations;
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
}

public interface IJobService
{
    List<Job> GetAll();
    List<Job> GetOpen();
    int CountAll();
    int CountOpen();
    Job? GetById(int id);
    Job Create(Job job);
    void Update(int id, Job input, int actorUserId);        // ATS-05
    void Close(int id, int actorUserId);                    // ATS-06
    void Reopen(int id, int actorUserId);
    /// <summary>Actor có quyền sửa/đóng/mở lại tin này không (Admin hoặc người tạo tin).</summary>
    bool CanModify(int jobId, int actorUserId);
    List<Job> Filter(string? category, string? techStack, string? level, string sort);  // ATS-07
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
    List<ApplicationStatusHistory> GetStatusHistory(int appId);
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

// ===== N1.G: số liệu cho dashboard Mentor =====

/// <summary>
/// Tổng quan một lần gọi cho dashboard. Gom vào một record thay vì 10 hàm đếm rời:
/// cả 10 con số phải đến từ CÙNG một lát cắt dữ liệu, nếu không tổng các trạng thái
/// có thể lệch khỏi tổng số đơn ngay trên cùng một màn hình.
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
    int? AiScore, int? HrScore, string Status, DateTime AppliedAt)
{
    public int? FinalScore => HrScore ?? AiScore;   // ATS-16.2
    public IReadOnlyList<string> SkillTagList => TechList.Parse(TechSkillTags);
}

public record MyApplicationItem(int Id, int JobId, string JobTitle, string Category, string Level,
    string CvFileNameSnapshot, int? AiScore, string? AiSource, string Status, DateTime AppliedAt);

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
    string GithubUrl, string LinkedInUrl, string PortfolioUrl, string TechSkillTags)
{
    public int? FinalScore => HrScore ?? AiScore;
    public bool HasAiEvaluation => AiScore.HasValue;
    public IReadOnlyList<string> SkillTagList => TechList.Parse(TechSkillTags);
    public AiResult Ai => new(AiScore, AiStrengths, AiMissing, AiRoadmap, AiSource);
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
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var total = db.AuditLogs.Count();
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
    }

    // ATS-07: lọc + sắp xếp (chỉ tin Open — dành cho Sinh viên IT)
    public List<Job> Filter(string? category, string? techStack, string? level, string sort)
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
    public List<ApplicantListItem> GetByJob(int jobId, string sort = "date")
    {
        var list = db.Applications.AsNoTracking()
            .Where(a => a.JobId == jobId)
            .Select(a => new ApplicantListItem(
                a.Id, a.CandidateProfile!.FullName, a.CandidateProfile.Email,
                a.CandidateProfile.TechSkillTags, a.AiScore, a.HrScore, a.Status, a.AppliedAt))
            .ToList();
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
                a.CvFileNameSnapshot, a.AiScore, a.AiSource, a.Status, a.AppliedAt))
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
                a.CandidateProfile.PortfolioUrl, a.CandidateProfile.TechSkillTags))
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
        a.AiStrengths = eval.Strengths;
        a.AiMissing = eval.Missing;
        a.AiRoadmap = eval.Roadmap;
        a.AiSource = eval.Source;      // Gemini hay Offline — hiển thị cho Mentor biết
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
    public bool UpdateStatus(int appId, string newStatus, int actorUserId, out string message)
    {
        if (!ApplicationStatus.All.Contains(newStatus))
        { message = "Trạng thái không hợp lệ."; return false; }

        var a = db.Applications.Include(x => x.Job)
                               .Include(x => x.CandidateProfile)
                               .FirstOrDefault(x => x.Id == appId);
        if (a is null) { message = "Không tìm thấy đơn."; return false; }
        if (a.Status == newStatus) { message = "Trạng thái không thay đổi."; return false; }

        using var tx = db.Database.BeginTransaction();
        try
        {
            var from = a.Status;
            db.ApplicationStatusHistories.Add(new ApplicationStatusHistory
            {
                ApplicationId = a.Id,
                FromStatus = from,
                ToStatus = newStatus,
                ChangedByUserId = actorUserId,
                ChangedAt = DateTime.Now
            });
            a.Status = newStatus;
            db.SaveChanges();

            // NTF-01: báo cho Sinh viên IT
            if (a.CandidateProfile != null)
                notify.Add(a.CandidateProfile.UserId,
                    "Cập nhật đơn ứng tuyển",
                    $"Đơn ứng tuyển vào '{a.Job?.Title}' đã chuyển sang trạng thái: {newStatus}.",
                    "/my-applications");

            audit?.Record(actorUserId, "Change Status", "Applications",
                $"Đơn #{appId}: {from} → {newStatus}");

            tx.Commit();
            message = "Đã cập nhật trạng thái.";
            return true;
        }
        catch
        {
            tx.Rollback();
            message = "Có lỗi khi cập nhật trạng thái.";
            return false;
        }
    }

    public List<ApplicationStatusHistory> GetStatusHistory(int appId) =>
        db.ApplicationStatusHistories.AsNoTracking()
                                     .Where(h => h.ApplicationId == appId)
                                     .OrderBy(h => h.ChangedAt).ToList();
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
            UserId = userId, Title = title, Message = message, Link = link,
            IsRead = false, CreatedAt = DateTime.Now
        });
        db.SaveChanges();
    }

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
