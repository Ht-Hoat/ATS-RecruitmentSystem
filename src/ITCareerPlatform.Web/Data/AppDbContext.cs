using ITCareerPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace ITCareerPlatform.Data;

// Cầu nối code <-> SQL Server (EF Core). Khớp ERD v2: 8 bảng.
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<Company> Companies => Set<Company>();   // P1-1
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<CandidateProfile> CandidateProfiles => Set<CandidateProfile>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<ApplicationStatusHistory> ApplicationStatusHistories => Set<ApplicationStatusHistory>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<SelfCheck> SelfChecks => Set<SelfCheck>();   // P1-2
    public DbSet<EmailOutbox> EmailOutbox => Set<EmailOutbox>();   // P1-3
    public DbSet<InterviewQuestionSnapshot> InterviewQuestionSnapshots => Set<InterviewQuestionSnapshot>();   // HIST: lịch sử câu hỏi PV

    // -----------------------------------------------------------------
    //  Đóng dấu CreatedAt/UpdatedAt một chỗ duy nhất.
    //  Trước đây mỗi service tự gán DateTime.Now, nên mỗi đường ghi mới
    //  chỉ cách một dòng bị quên là có mốc thời gian sai.
    // -----------------------------------------------------------------
    public override int SaveChanges()
    {
        StampTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampTimestamps()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<ITimestamped>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAt == default) entry.Entity.CreatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                // CreatedAt là bất biến — chặn mọi lần ghi đè vô tình.
                entry.Property(e => e.CreatedAt).IsModified = false;
            }
        }

        // P0-2: Đóng dấu tập trung cho các entity không kế thừa ITimestamped nhưng có mốc thời gian
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added) continue;

            if (entry.Entity is AuditLog audit && audit.Timestamp == default)
                audit.Timestamp = now;
            else if (entry.Entity is Application app && app.AppliedAt == default)
                app.AppliedAt = now;
            else if (entry.Entity is Notification notif && notif.CreatedAt == default)
                notif.CreatedAt = now;
            else if (entry.Entity is ApplicationStatusHistory hist && hist.ChangedAt == default)
                hist.ChangedAt = now;
            else if (entry.Entity is SelfCheck self && self.CreatedAt == default)
                self.CreatedAt = now;
            else if (entry.Entity is EmailOutbox mail && mail.CreatedAt == default)
                mail.CreatedAt = now;
        }
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Email duy nhất
        b.Entity<User>().HasIndex(u => u.Email).IsUnique();

        // Roles (1) --- (n) Users
        b.Entity<User>()
            .HasOne(u => u.Role).WithMany(r => r.Users)
            .HasForeignKey(u => u.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        // Users (1) --- (n) Jobs
        b.Entity<Job>()
            .HasOne(j => j.CreatedBy).WithMany(u => u.CreatedJobs)
            .HasForeignKey(j => j.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        // Users (1) --- (n) AuditLogs
        b.Entity<AuditLog>()
            .HasOne(a => a.User).WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // P1-1: Companies (1) --- (n) Jobs, và (1) --- (n) Users (tài khoản Mentor).
        // Restrict ở cả hai chiều: xóa một công ty đang có tin hoặc đang có nhân sự phải là
        // thao tác có ý thức, không được kéo theo cả tin tuyển dụng lẫn tài khoản.
        b.Entity<Job>()
            .HasOne(j => j.Company).WithMany(c => c.Jobs)
            .HasForeignKey(j => j.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<User>()
            .HasOne(u => u.Company).WithMany()
            .HasForeignKey(u => u.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<Company>().HasIndex(c => c.Name);
        b.Entity<Job>().HasIndex(j => j.CompanyId);

        b.Entity<Job>().Property(j => j.SalaryMin).HasColumnType("decimal(18,2)");
        b.Entity<Job>().Property(j => j.SalaryMax).HasColumnType("decimal(18,2)");
        b.Entity<Job>().Property(j => j.EmploymentType).HasMaxLength(20).HasDefaultValue("Onsite");

        // Lọc tin theo trạng thái + hạn nộp (ATS-07) và liệt kê tin của một Mentor (ATS-05)
        b.Entity<Job>().HasIndex(j => new { j.Status, j.Deadline });
        b.Entity<Job>().HasIndex(j => j.CreatedById);

        // Users (1) --- (1) CandidateProfile
        b.Entity<CandidateProfile>()
            .HasOne(p => p.User).WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<CandidateProfile>().HasIndex(p => p.UserId).IsUnique();

        // Jobs (1) --- (n) Applications
        b.Entity<Application>()
            .HasOne(a => a.Job).WithMany(j => j.Applications)
            .HasForeignKey(a => a.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        // CandidateProfile (1) --- (n) Applications
        b.Entity<Application>()
            .HasOne(a => a.CandidateProfile).WithMany(p => p.Applications)
            .HasForeignKey(a => a.CandidateProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        // ATS-10: 1 ứng viên chỉ nộp 1 lần / 1 tin
        b.Entity<Application>().HasIndex(a => new { a.JobId, a.CandidateProfileId }).IsUnique();

        // Thống kê theo trạng thái (ATS-18)
        b.Entity<Application>().HasIndex(a => a.Status);

        // Application (1) --- (n) StatusHistory
        b.Entity<ApplicationStatusHistory>()
            .HasOne(h => h.Application).WithMany(a => a.StatusHistory)
            .HasForeignKey(h => h.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        // P1-2: SelfChecks. Cascade theo cả User lẫn Job vì bản ghi này không có ý nghĩa
        // độc lập — xóa tài khoản hay xóa tin thì kết quả tự kiểm cũng hết chỗ để đọc.
        b.Entity<SelfCheck>()
            .HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<SelfCheck>()
            .HasOne(x => x.Job).WithMany()
            .HasForeignKey(x => x.JobId)
            .OnDelete(DeleteBehavior.Restrict);
        // Hai truy vấn duy nhất của bảng này: đếm hạn mức trong ngày của một người, và lấy
        // bản gần nhất của (người, tin). Index phủ cả hai.
        b.Entity<SelfCheck>().HasIndex(x => new { x.UserId, x.CreatedAt });
        b.Entity<SelfCheck>().HasIndex(x => new { x.UserId, x.JobId, x.Id });

        // P1-3: tiến trình nền chỉ hỏi đúng một câu — "email nào chưa gửi và chưa quá số lần
        // thử" — nên index phủ đúng hai cột đó. Bảng này chỉ tăng, không có index thì mỗi
        // lần quét (30 giây một lần) là một lần đọc toàn bảng.
        b.Entity<EmailOutbox>().HasIndex(x => new { x.SentAt, x.Attempts });

        // Notifications: index theo người nhận để truy vấn nhanh
        b.Entity<Notification>().HasIndex(n => new { n.UserId, n.IsRead });

        // Nhật ký hệ thống đọc theo thứ tự mới nhất, có phân trang
        b.Entity<AuditLog>().HasIndex(a => a.Timestamp);

        // ===== Seed 3 vai trò IT Career Platform =====
        // Dùng ITCareerPlatform.Models.Roles để tránh bị DbSet<Role> Roles property che khuất
        b.Entity<Role>().HasData(
            new Role { Id = Models.Roles.AdminId, RoleName = Models.Roles.Admin, Description = "Quản trị toàn hệ thống IT Career Platform" },
            new Role { Id = Models.Roles.MentorId, RoleName = Models.Roles.Mentor, Description = "Cố vấn tuyển dụng IT — đăng việc, xem hồ sơ SV" },
            new Role { Id = Models.Roles.StudentId, RoleName = Models.Roles.Student, Description = "Sinh viên IT sắp tốt nghiệp — tìm việc, tạo hồ sơ, ứng tuyển" }
        );
    }
}
