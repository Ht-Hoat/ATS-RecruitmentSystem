using ITCareerPlatform.Models;
using ITCareerPlatform.Services;

namespace ITCareerPlatform.Data;

// Dữ liệu mẫu IT Career Platform (chạy sau khi CSDL được tạo).
public static class SeedData
{
    public static void Initialize(AppDbContext db)
    {
        if (db.Users.Any()) return;

        string H(string p) => BCrypt.Net.BCrypt.HashPassword(p);

        // ===== Tài khoản =====
        var admin = new User { FullName = "Hoàng Thị Hoạt", Email = "admin@itcp.vn", PasswordHash = H("123456"), RoleId = Roles.AdminId, IsActive = true };
        var mentor = new User { FullName = "Nguyễn Việt Hùng", Email = "mentor@itcp.vn", PasswordHash = H("123456"), RoleId = Roles.MentorId, IsActive = true };
        var mentor2 = new User { FullName = "Trần Văn Tiền", Email = "mentor2@itcp.vn", PasswordHash = H("123456"), RoleId = Roles.MentorId, IsActive = true };
        var locked = new User { FullName = "SV bị khóa", Email = "locked@itcp.vn", PasswordHash = H("123456"), RoleId = Roles.StudentId, IsActive = false };
        var lan = new User { FullName = "Phạm Thị Lan", Email = "lan@itcp.vn", PasswordHash = H("123456"), RoleId = Roles.StudentId, IsActive = true };
        var khoa = new User { FullName = "Đỗ Văn Khoa", Email = "khoa@itcp.vn", PasswordHash = H("123456"), RoleId = Roles.StudentId, IsActive = true };
        db.Users.AddRange(admin, mentor, mentor2, locked, lan, khoa);
        db.SaveChanges();

        // ===== Tin việc làm IT (ATS-04: có Category, TechStack, Level) =====
        var jBackend = new Job
        {
            Title = "Lập trình viên Backend .NET", Category = "Backend", Level = "Junior",
            TechStack = "C#, .NET, ASP.NET Core, SQL Server, Docker",
            Description = "Phát triển REST API và hệ thống web bằng ASP.NET Core cho sản phẩm tuyển dụng IT.",
            Requirements = "Thành thạo C#, EF Core, SQL Server; hiểu Docker; 1 năm kinh nghiệm.",
            Location = "Hà Nội", SalaryMin = 15, SalaryMax = 25, Deadline = DateTime.Today.AddDays(20),
            Status = "Open", CreatedById = mentor.Id, CreatedAt = DateTime.Now.AddDays(-5)
        };
        var jFrontend = new Job
        {
            Title = "Frontend Developer (React)", Category = "Frontend", Level = "Middle",
            TechStack = "JavaScript, React, TypeScript, HTML, CSS",
            Description = "Xây dựng giao diện người dùng bằng React cho nền tảng IT Career.",
            Requirements = "React, TypeScript, kinh nghiệm 2 năm; hiểu REST API.",
            Location = "TP.HCM", SalaryMin = 18, SalaryMax = 30, Deadline = DateTime.Today.AddDays(4), // sắp hết hạn
            Status = "Open", CreatedById = mentor.Id, CreatedAt = DateTime.Now.AddDays(-3)
        };
        var jDevOps = new Job
        {
            Title = "DevOps Engineer", Category = "DevOps", Level = "Senior",
            TechStack = "Linux, Docker, Kubernetes, CI/CD, Azure",
            Description = "Vận hành hạ tầng, xây dựng pipeline CI/CD.",
            Requirements = "Docker, Kubernetes, GitHub Actions; 3 năm kinh nghiệm.",
            Location = "Hà Nội", SalaryMin = 30, SalaryMax = 45, Deadline = DateTime.Today.AddDays(25),
            Status = "Open", CreatedById = mentor2.Id, CreatedAt = DateTime.Now.AddDays(-2)
        };
        var jDataAi = new Job
        {
            Title = "Kỹ sư Data/AI (NLP)", Category = "Data/AI", Level = "Junior",
            TechStack = "Python, SQL, Machine Learning, LLM API",
            Description = "Xây dựng module sàng lọc CV bằng AI.",
            Requirements = "Python, SQL, hiểu ML cơ bản, LLM API.",
            Location = "TP.HCM", SalaryMin = 20, SalaryMax = 35, Deadline = DateTime.Today.AddDays(15),
            Status = "Open", CreatedById = mentor.Id, CreatedAt = DateTime.Now.AddDays(-1)
        };
        var jIntern = new Job
        {
            Title = "Thực tập sinh Kiểm thử (QA)", Category = "QA", Level = "Intern",
            TechStack = "Manual Testing, SQL, Postman",
            Description = "Kiểm thử chức năng hệ thống.",
            Requirements = "Sinh viên năm cuối CNTT.",
            Location = "Hà Nội", SalaryMin = 3, SalaryMax = 5, Deadline = DateTime.Today.AddDays(-2),
            Status = "Closed", CreatedById = mentor.Id, CreatedAt = DateTime.Now.AddDays(-10)
        };
        db.Jobs.AddRange(jBackend, jFrontend, jDevOps, jDataAi, jIntern);
        db.SaveChanges();

        // ===== Hồ sơ Sinh viên IT (ATS-08: có GitHub/LinkedIn/Portfolio/TechSkillTags) =====
        byte[] SampleCv(string name) =>
            System.Text.Encoding.UTF8.GetBytes(
                "%PDF-1.4\n% CV mau - IT Career Platform - " + name +
                "\nKy nang: C#, .NET, React, SQL Server, Docker. Kinh nghiem lam viec du an web.\n");

        var pLan = new CandidateProfile
        {
            UserId = lan.Id, FullName = "Phạm Thị Lan", Email = "lan@itcp.vn", Phone = "0912345678",
            DateOfBirth = new DateTime(2002, 5, 12), Address = "Cầu Giấy, Hà Nội",
            Education = "Cử nhân CNTT - ĐH Bách Khoa Hà Nội (2025)",
            Experience = "1 năm thực tập lập trình web .NET tại công ty phần mềm ABC.",
            Skills = "Lập trình web, làm việc nhóm, tiếng Anh giao tiếp",
            TechSkillTags = "C#,.NET,ASP.NET Core,SQL Server,Docker",
            GithubUrl = "https://github.com/phamthilan", LinkedInUrl = "https://linkedin.com/in/phamthilan",
            PortfolioUrl = "https://phamthilan.dev",
            CvData = SampleCv("Pham Thi Lan"), CvFileName = "CV_PhamThiLan.pdf",
            CvContentType = "application/pdf", CvUploadedAt = DateTime.Now
        };
        var pKhoa = new CandidateProfile
        {
            UserId = khoa.Id, FullName = "Đỗ Văn Khoa", Email = "khoa@itcp.vn", Phone = "0987654321",
            DateOfBirth = new DateTime(2001, 11, 3), Address = "Thanh Xuân, Hà Nội",
            Education = "Cử nhân Khoa học máy tính - ĐH Công nghệ (2024)",
            Experience = "2 năm phát triển frontend React tại startup XYZ.",
            Skills = "React, TypeScript, UI/UX cơ bản",
            TechSkillTags = "JavaScript,React,TypeScript,HTML,CSS",
            GithubUrl = "https://github.com/dovankhoa", LinkedInUrl = "https://linkedin.com/in/dovankhoa",
            PortfolioUrl = "https://khoa.dev",
            CvData = SampleCv("Do Van Khoa"), CvFileName = "CV_DoVanKhoa.pdf",
            CvContentType = "application/pdf", CvUploadedAt = DateTime.Now
        };
        db.CandidateProfiles.AddRange(pLan, pKhoa);
        db.SaveChanges();

        // ===== Đơn ứng tuyển mẫu (ATS-10) + điểm AI mẫu (heuristic offline) =====
        Application MakeApp(Job job, CandidateProfile p, int daysAgo)
        {
            var candidateText = $"{p.TechSkillTags} {p.Skills} {p.Experience} {p.Education}";
            var jobText = $"{job.TechStack} {job.Requirements} {job.Description} {job.Level} {job.Category}";
            var e = GeminiAiService.HeuristicEvaluate(candidateText, jobText);
            return new Application
            {
                JobId = job.Id, CandidateProfileId = p.Id,
                CvFileNameSnapshot = p.CvFileName!, CvDataSnapshot = p.CvData, CvContentTypeSnapshot = p.CvContentType,
                Status = ApplicationStatus.Submitted, AppliedAt = DateTime.Now.AddDays(-daysAgo),
                AiScore = e.MatchPercent, AiStrengths = e.Strengths, AiMissing = e.Missing,
                AiRoadmap = e.Roadmap, AiSummary = e.Raw, AiScoredAt = DateTime.Now
            };
        }
        // Lan (Backend stack) ứng tuyển Backend → điểm cao; Khoa (React) ứng tuyển Backend → điểm thấp hơn
        db.Applications.AddRange(
            MakeApp(jBackend, pLan, 1),
            MakeApp(jBackend, pKhoa, 0),
            MakeApp(jFrontend, pKhoa, 2)
        );
        db.SaveChanges();
    }
}
