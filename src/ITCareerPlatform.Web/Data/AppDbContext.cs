using ITCareerPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace ITCareerPlatform.Data;

// Cầu nối code <-> SQL Server (EF Core). Khớp ERD v2: 8 bảng.
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<CandidateProfile> CandidateProfiles => Set<CandidateProfile>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<ApplicationStatusHistory> ApplicationStatusHistories => Set<ApplicationStatusHistory>();
    public DbSet<Notification> Notifications => Set<Notification>();

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

        b.Entity<Job>().Property(j => j.SalaryMin).HasColumnType("decimal(18,2)");
        b.Entity<Job>().Property(j => j.SalaryMax).HasColumnType("decimal(18,2)");

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

        // Application (1) --- (n) StatusHistory
        b.Entity<ApplicationStatusHistory>()
            .HasOne(h => h.Application).WithMany(a => a.StatusHistory)
            .HasForeignKey(h => h.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Notifications: index theo người nhận để truy vấn nhanh
        b.Entity<Notification>().HasIndex(n => new { n.UserId, n.IsRead });

        // ===== Seed 3 vai trò IT Career Platform =====
        // Dùng ITCareerPlatform.Models.Roles để tránh bị DbSet<Role> Roles property che khuất
        b.Entity<Role>().HasData(
            new Role { Id = ITCareerPlatform.Models.Roles.AdminId, RoleName = ITCareerPlatform.Models.Roles.Admin, Description = "Quản trị toàn hệ thống IT Career Platform" },
            new Role { Id = ITCareerPlatform.Models.Roles.MentorId, RoleName = ITCareerPlatform.Models.Roles.Mentor, Description = "Cố vấn tuyển dụng IT — đăng việc, xem hồ sơ SV" },
            new Role { Id = ITCareerPlatform.Models.Roles.StudentId, RoleName = ITCareerPlatform.Models.Roles.Student, Description = "Sinh viên IT sắp tốt nghiệp — tìm việc, tạo hồ sơ, ứng tuyển" }
        );
    }
}
