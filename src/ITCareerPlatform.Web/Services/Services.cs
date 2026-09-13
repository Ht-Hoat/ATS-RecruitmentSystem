using ITCareerPlatform.Data;
using ITCareerPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace ITCareerPlatform.Services;

// =====================================================================
//  INTERFACES (khớp Class Diagram v2 — Controllers/Components phụ thuộc interface)
// =====================================================================

public interface IAuthService { User? Validate(string email, string password); }

public interface IUserService
{
    List<User> GetAll();
    User? GetById(int id);
    User Create(string fullName, string email, string password, int roleId);
    void ToggleLock(int id);
    void ChangeRole(int id, int roleId, int actorUserId);
    bool Register(string fullName, string email, string password, out string error);   // EXT-01
}

public interface IJobService
{
    List<Job> GetAll();
    List<Job> GetOpen();
    Job? GetById(int id);
    Job Create(Job job);
    void Update(int id, Job input, int actorUserId);   // ATS-05
    void Close(int id);
    void Reopen(int id);
    // ATS-07 + N2.C: lọc việc IT theo Category + TechStack + Level + Lương + Hình thức + Địa điểm + sắp xếp
    List<Job> Filter(string? category, string? techStack, string? level, string sort,
        decimal? minSalary = null, string? employmentType = null, string? location = null);
}

public interface IProfileService
{
    CandidateProfile? GetByUserId(int userId);
    CandidateProfile? GetById(int id);
    CandidateProfile Save(int userId, CandidateProfile input);            // ATS-08
    (bool ok, string? error) SaveCv(int userId, byte[] data, string fileName, string contentType); // ATS-09 + SEC-01
}

public interface IApplicationService
{
    bool Apply(int jobId, int candidateUserId, out string message);       // ATS-10
    List<ApplicantListItem> GetByJob(int jobId, string sort = "date");     // ATS-11 + ATS-15 (không tải CvData)
    List<MyApplicationItem> GetByCandidate(int userId);                    // (không tải CvData)
    Application? GetById(int id);                                          // ATS-12 (đầy đủ, cho 1 đơn)
    int CountByJob(int jobId);
    Dictionary<int, int> CountAllByJob();                                  // gộp đếm 1 lần, tránh N+1
    bool CanAccess(int appId, int actorUserId, bool isAdmin);             // Mentor chỉ được tin mình tạo
    void SaveAiEvaluation(int appId, AiEvaluation eval);                   // ATS-13/14
    void SaveHrScore(int appId, int hrScore, string note);                // ATS-16
    bool UpdateStatus(int appId, string newStatus, int actorUserId, out string message); // ATS-17
    List<ApplicationStatusHistory> GetStatusHistory(int appId);
    int CountRecentApplicants(int actorUserId, bool isAdmin, int withinHours);
    int CountUnreviewed(int actorUserId, bool isAdmin);
    List<MentorApplicantItem> TopUnreviewed(int actorUserId, bool isAdmin, int take);
}

// DTO nhẹ cho danh sách — CHỈ các cột cần hiển thị, KHÔNG kèm byte[] CV (chống nghẽn RAM/băng thông)
public record ApplicantListItem(int Id, string FullName, string Email, string TechSkillTags,
    int? AiScore, int? HrScore, string Status, DateTime AppliedAt)
{
    public int? FinalScore => HrScore ?? AiScore;   // ATS-16.2
}

public record MentorApplicantItem(int Id, string FullName, string Email, string TechSkillTags,
    int? AiScore, int? HrScore, string Status, DateTime AppliedAt, int JobId, string JobTitle)
{
    public int? FinalScore => HrScore ?? AiScore;
}

public record MyApplicationItem(int Id, int JobId, string JobTitle, string Category, string Level,
    string CvFileNameSnapshot, int? AiScore, string Status, DateTime AppliedAt);

public interface INotificationService                                     // NTF-01
{
    void Add(int userId, string title, string message, string link);
    List<Notification> GetForUser(int userId, int take = 20);
    int CountUnread(int userId);
    void MarkRead(int id);
}

// =====================================================================
//  AuthService (mật khẩu băm BCrypt)
// =====================================================================
public class AuthService(AppDbContext db) : IAuthService
{
    public User? Validate(string email, string password)
    {
        var u = db.Users.Include(x => x.Role)
                        .FirstOrDefault(x => x.Email.ToLower() == email.ToLower());
        if (u is null || !u.IsActive) return null;
        return BCrypt.Net.BCrypt.Verify(password, u.PasswordHash) ? u : null;
    }
}

// =====================================================================
//  UserService (ATS-01, ATS-02, EXT-01)
// =====================================================================
public class UserService(AppDbContext db) : IUserService
{
    public List<User> GetAll() => db.Users.Include(u => u.Role).OrderBy(u => u.Id).ToList();
    public User? GetById(int id) => db.Users.Find(id);

    public User Create(string fullName, string email, string password, int roleId)
    {
        var u = new User
        {
            FullName = fullName,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            RoleId = roleId,
            IsActive = true
        };
        db.Users.Add(u);
        db.SaveChanges();
        return u;
    }

    public void ToggleLock(int id)
    {
        var u = db.Users.Find(id);
        if (u is null) return;
        u.IsActive = !u.IsActive;
        u.UpdatedAt = DateTime.Now;
        db.SaveChanges();
    }

    public void ChangeRole(int id, int roleId, int actorUserId)
    {
        var u = db.Users.Find(id);
        if (u is null) return;
        var oldRole = db.Roles.Find(u.RoleId)?.RoleName ?? "?";
        var newRole = db.Roles.Find(roleId)?.RoleName ?? "?";
        u.RoleId = roleId;
        u.UpdatedAt = DateTime.Now;
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
        email = (email ?? "").Trim();
        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        { error = "Vui lòng nhập đầy đủ họ tên, email và mật khẩu."; return false; }
        if (password.Length < 8)
        { error = "Mật khẩu phải có tối thiểu 8 ký tự."; return false; }
        if (db.Users.Any(u => u.Email.ToLower() == email.ToLower()))
        { error = "Email này đã được đăng ký, vui lòng dùng email khác hoặc đăng nhập."; return false; }

        db.Users.Add(new User
        {
            FullName = fullName.Trim(),
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            RoleId = Roles.StudentId,
            IsActive = true
        });
        db.SaveChanges();
        return true;
    }
}

// =====================================================================
//  JobService (ATS-04, ATS-05, ATS-06, ATS-07)
// =====================================================================
public class JobService(AppDbContext db) : IJobService
{
    public List<Job> GetAll() =>
        db.Jobs.OrderByDescending(j => j.CreatedAt).ThenByDescending(j => j.Id).ToList();

    public List<Job> GetOpen() =>
        db.Jobs.Where(j => j.Status == "Open")
               .OrderByDescending(j => j.CreatedAt).ThenByDescending(j => j.Id).ToList();

    public Job? GetById(int id) => db.Jobs.Find(id);

    public Job Create(Job job)
    {
        // ATS-04.3: validate nghiệp vụ
        if (string.IsNullOrWhiteSpace(job.Title))
            throw new ArgumentException("Tiêu đề công việc không được để trống.");
        if (job.SalaryMax > 0 && job.SalaryMin > 0 && job.SalaryMax < job.SalaryMin)
            throw new ArgumentException("Lương tối đa phải ≥ lương tối thiểu.");

        job.Status = "Open";
        job.CreatedAt = DateTime.Now;
        db.Jobs.Add(job);
        db.SaveChanges();
        return job;
    }

    // ATS-05: chỉ người tạo hoặc Admin mới sửa; không sửa tin Closed
    public void Update(int id, Job input, int actorUserId)
    {
        var j = db.Jobs.Find(id) ?? throw new InvalidOperationException("Không tìm thấy tin.");
        var actor = db.Users.Find(actorUserId);
        var isAdmin = actor?.RoleId == Roles.AdminId;
        if (!isAdmin && j.CreatedById != actorUserId)
            throw new UnauthorizedAccessException("Bạn không có quyền sửa tin này.");
        if (j.Status == "Closed")
            throw new InvalidOperationException("Tin đã đóng, vui lòng mở lại trước khi sửa.");

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
        j.UpdatedAt = DateTime.Now;
        db.SaveChanges();
    }

    public void Close(int id)
    {
        var j = db.Jobs.Find(id);
        if (j is null) return;
        j.Status = "Closed"; j.UpdatedAt = DateTime.Now;
        db.SaveChanges();
    }

    public void Reopen(int id)
    {
        var j = db.Jobs.Find(id);
        if (j is null) return;
        j.Status = "Open"; j.UpdatedAt = DateTime.Now;
        db.SaveChanges();
    }

    // ATS-07 + N2.C: lọc + sắp xếp (chỉ tin Open — dành cho Sinh viên IT)
    public List<Job> Filter(string? category, string? techStack, string? level, string sort,
        decimal? minSalary = null, string? employmentType = null, string? location = null)
    {
        var today = DateTime.Today;
        // #9: chỉ hiện tin Open và CÒN hạn nộp
        var q = db.Jobs.Where(j => j.Status == "Open" && j.Deadline >= today);

        if (!string.IsNullOrWhiteSpace(category) && category != "Tất cả")
            q = q.Where(j => j.Category == category);

        if (!string.IsNullOrWhiteSpace(level) && level != "Tất cả")
            q = q.Where(j => j.Level == level);

        if (!string.IsNullOrWhiteSpace(techStack))
        {
            var kw = techStack.Trim().ToLower();
            // LIKE '%kw%' không phân biệt hoa thường
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

    public CandidateProfile? GetById(int id) =>
        db.CandidateProfiles.Include(p => p.User).FirstOrDefault(p => p.Id == id);

    public CandidateProfile Save(int userId, CandidateProfile input)
    {
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
            p = new CandidateProfile { UserId = userId, CreatedAt = DateTime.Now };
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
        p.UpdatedAt = DateTime.Now;
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
            p = new CandidateProfile { UserId = userId, CreatedAt = DateTime.Now };
            var u = db.Users.Find(userId);
            if (u != null) { p.FullName = u.FullName; p.Email = u.Email; }
            db.CandidateProfiles.Add(p);
        }
        p.CvData = data;
        p.CvFileName = fileName;
        p.CvContentType = contentType;
        p.CvUploadedAt = DateTime.Now;
        p.UpdatedAt = DateTime.Now;
        db.SaveChanges();
        return (true, null);
    }
}

// =====================================================================
//  ApplicationService (ATS-10 → ATS-17)
// =====================================================================
public class ApplicationService(AppDbContext db, INotificationService notify) : IApplicationService
{
    // ATS-10.2: 3 lớp kiểm tra nghiệp vụ
    public bool Apply(int jobId, int candidateUserId, out string message)
    {
        var job = db.Jobs.Find(jobId);
        if (job is null) { message = "Không tìm thấy tin tuyển dụng."; return false; }
        if (job.Status != "Open") { message = "Vị trí tuyển dụng này đã đóng, không nhận hồ sơ."; return false; }
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
            // #11: hai request cùng nộp một lúc → unique index (JobId, CandidateProfileId) chặn,
            // báo thân thiện thay vì để lỗi 500.
            message = "Bạn đã ứng tuyển vào vị trí này rồi.";
            return false;
        }
        message = "Ứng tuyển thành công!";
        return true;
    }

    // ATS-11 + ATS-15: danh sách ứng viên (projection — KHÔNG kéo byte[] CV về)
    public List<ApplicantListItem> GetByJob(int jobId, string sort = "date")
    {
        var list = db.Applications
            .Where(a => a.JobId == jobId)
            .Select(a => new ApplicantListItem(
                a.Id, a.CandidateProfile!.FullName, a.CandidateProfile.Email,
                a.CandidateProfile.TechSkillTags, a.AiScore, a.HrScore, a.Status, a.AppliedAt))
            .ToList();
        return sort == "score"
            ? list.OrderByDescending(x => x.FinalScore ?? -1).ThenByDescending(x => x.AppliedAt).ToList()
            : list.OrderByDescending(x => x.AppliedAt).ToList();
    }

    // Đơn của sinh viên (projection — KHÔNG kéo byte[] CV về)
    public List<MyApplicationItem> GetByCandidate(int userId) =>
        db.Applications
            .Where(a => a.CandidateProfile!.UserId == userId)
            .OrderByDescending(a => a.AppliedAt)
            .Select(a => new MyApplicationItem(
                a.Id, a.JobId, a.Job!.Title, a.Job.Category, a.Job.Level,
                a.CvFileNameSnapshot, a.AiScore, a.Status, a.AppliedAt))
            .ToList();

    public Application? GetById(int id) =>
        db.Applications.Include(a => a.Job)
                       .Include(a => a.CandidateProfile).ThenInclude(p => p!.User)
                       .FirstOrDefault(a => a.Id == id);

    public int CountByJob(int jobId) => db.Applications.Count(a => a.JobId == jobId);

    // #2: đếm số ứng viên cho TẤT CẢ tin trong 1 truy vấn GROUP BY (thay vì N+1)
    public Dictionary<int, int> CountAllByJob() =>
        db.Applications.GroupBy(a => a.JobId)
                       .Select(g => new { JobId = g.Key, Count = g.Count() })
                       .ToDictionary(x => x.JobId, x => x.Count);

    // #5: Mentor chỉ được xem/thao tác đơn thuộc tin do mình tạo; Admin xem tất cả
    public bool CanAccess(int appId, int actorUserId, bool isAdmin)
    {
        if (isAdmin) return true;
        var ownerId = db.Applications.Where(a => a.Id == appId)
                                     .Select(a => (int?)a.Job!.CreatedById)
                                     .FirstOrDefault();
        return ownerId == actorUserId;
    }

    // ATS-13/14: lưu kết quả đánh giá AI (% phù hợp + điểm mạnh/thiếu/lộ trình) — không đụng HrScore
    public void SaveAiEvaluation(int appId, AiEvaluation eval)
    {
        var a = db.Applications.Find(appId);
        if (a is null) return;
        a.AiScore = Math.Clamp(eval.MatchPercent, 0, 100);
        a.AiStrengths = eval.Strengths;
        a.AiMissing = eval.Missing;
        a.AiRoadmap = eval.Roadmap;
        a.AiSummary = eval.Raw;
        a.AiScoredAt = DateTime.Now;
        db.SaveChanges();
    }

    // ATS-16: Mentor điều chỉnh điểm (không đụng tới AiScore)
    public void SaveHrScore(int appId, int hrScore, string note)
    {
        var a = db.Applications.Find(appId);
        if (a is null) return;
        a.HrScore = Math.Clamp(hrScore, 0, 100);
        a.HrNote = note;
        a.HrAdjustedAt = DateTime.Now;
        db.SaveChanges();
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
        db.ApplicationStatusHistories.Where(h => h.ApplicationId == appId)
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
            UserId = userId, Title = title, Message = message, Link = link,
            IsRead = false, CreatedAt = DateTime.Now
        });
        db.SaveChanges();
    }

    public List<Notification> GetForUser(int userId, int take = 20) =>
        db.Notifications.Where(n => n.UserId == userId)
                        .OrderByDescending(n => n.CreatedAt).Take(take).ToList();

    public int CountUnread(int userId) =>
        db.Notifications.Count(n => n.UserId == userId && !n.IsRead);

    public void MarkRead(int id)
    {
        var n = db.Notifications.Find(id);
        if (n is null) return;
        n.IsRead = true;
        db.SaveChanges();
    }
}
