using ITCareerPlatform.Models;
using ITCareerPlatform.Services;

namespace ITCareerPlatform.Data;

// Dữ liệu mẫu IT Career Platform (chạy sau khi CSDL được tạo).
// P0-2: dữ liệu mẫu tuân đúng quy ước thời gian của hệ thống — mốc THỜI ĐIỂM lưu ở UTC,
// còn hạn nộp là một NGÀY trên tờ lịch Việt Nam. Gieo bằng giờ máy chủ sẽ làm dữ liệu mẫu
// lệch 7 tiếng so với dữ liệu do chính ứng dụng sinh ra, và mọi màn hình thống kê đọc hai
// nguồn đó như nhau.
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

        // ===== P1-1: Công ty =====
        // Hai công ty khác nhau cho hai Mentor, để demo và test nhìn thấy được ranh giới:
        // một tin đứng tên ai, và danh sách tin của Mentor này không lẫn sang Mentor kia.
        var fpt = new Company
        {
            Name = "FPT Software", Website = "https://fptsoftware.com",
            Address = "Tòa FPT, Duy Tân, Cầu Giấy, Hà Nội",
            Description = "Công ty phần mềm lớn nhất Việt Nam, tuyển thực tập sinh và kỹ sư mới ra trường cho các dự án Nhật - Mỹ - EU."
        };
        var vng = new Company
        {
            Name = "VNG Corporation", Website = "https://vng.com.vn",
            Address = "Z06 Đường số 13, Tân Thuận Đông, Quận 7, TP.HCM",
            Description = "Công ty công nghệ về game, thanh toán và điện toán đám mây; môi trường sản phẩm quy mô hàng chục triệu người dùng."
        };
        db.Companies.AddRange(fpt, vng);
        db.SaveChanges();

        mentor.CompanyId = fpt.Id;
        mentor2.CompanyId = vng.Id;
        // Admin cũng đăng được tin (endpoint cho phép Admin, Mentor), nên phải có công ty —
        // nếu không, tài khoản quản trị mở form đăng tin rồi mới bị từ chối ở bước lưu.
        admin.CompanyId = fpt.Id;
        db.SaveChanges();

        // ===== Tin việc làm IT (ATS-04: có Category, TechStack, Level) =====
        var jBackend = new Job
        {
            Title = "Lập trình viên Backend .NET", Category = "Backend", Level = "Junior",
            TechStack = "C#, .NET, ASP.NET Core, SQL Server, Docker",
            Description = """
                Tham gia đội phát triển nền tảng tuyển dụng IT phục vụ hàng nghìn sinh viên và doanh nghiệp.

                Công việc chính:
                - Thiết kế và phát triển REST API bằng ASP.NET Core cho các module tin tuyển dụng, hồ sơ ứng viên, đơn ứng tuyển.
                - Làm việc với SQL Server qua EF Core: thiết kế bảng, viết migration, tối ưu truy vấn chậm.
                - Viết unit test (xUnit) cho tầng nghiệp vụ; tham gia code review cùng team.
                - Đóng gói dịch vụ bằng Docker, phối hợp DevOps đưa lên môi trường staging và production.

                Quyền lợi:
                - Làm việc hybrid 3 ngày văn phòng, 2 ngày tại nhà.
                - Mentor 1-1 trong 3 tháng đầu; ngân sách học tập và chứng chỉ hằng năm.
                - Review lương 2 lần/năm, thưởng dự án, bảo hiểm sức khỏe.
                """,
            Requirements = """
                - Tối thiểu 1 năm kinh nghiệm lập trình C# / .NET (tính cả thực tập).
                - Nắm vững ASP.NET Core Web API, EF Core, LINQ và SQL Server.
                - Hiểu nguyên lý REST, xác thực bằng cookie/JWT, xử lý lỗi và ghi log.
                - Biết dùng Git, Docker ở mức build và chạy container.
                - Ưu tiên: đã viết unit test, từng làm với Azure hoặc CI/CD.
                """,
            Location = "Hà Nội", SalaryMin = 15, SalaryMax = 25, Deadline = VietnamDateHelper.Today().AddDays(20),
            EmploymentType = "Hybrid",
            Status = JobStatus.Open, CreatedById = mentor.Id, CompanyId = fpt.Id, CreatedAt = DateTime.UtcNow.AddDays(-5)
        };
        var jFrontend = new Job
        {
            Title = "Frontend Developer (React)", Category = "Frontend", Level = "Middle",
            TechStack = "JavaScript, React, TypeScript, HTML, CSS",
            Description = """
                Phát triển giao diện web cho nền tảng IT Career — nơi sinh viên tìm việc và nhà tuyển dụng sàng lọc hồ sơ.

                Công việc chính:
                - Xây dựng các màn hình tìm việc, hồ sơ ứng viên, dashboard nhà tuyển dụng bằng React + TypeScript.
                - Tích hợp REST API do team backend cung cấp; xử lý trạng thái tải, lỗi và phân trang.
                - Đảm bảo giao diện responsive, đạt chuẩn truy cập cơ bản (WCAG AA).
                - Viết component tái sử dụng và tài liệu cho design system nội bộ.

                Quyền lợi:
                - Làm việc onsite tại TP.HCM, MacBook cấp riêng.
                - 13 tháng lương, thưởng theo hiệu quả dự án.
                - Được tham gia các buổi tech talk và hội thảo frontend trong nước.
                """,
            Requirements = """
                - Tối thiểu 2 năm kinh nghiệm với React (hooks, context, router).
                - Thành thạo TypeScript, HTML5, CSS3; biết Flexbox/Grid.
                - Hiểu cách gọi REST API, xử lý bất đồng bộ và quản lý state.
                - Có ý thức về hiệu năng (lazy loading, memo) và trải nghiệm người dùng.
                - Ưu tiên: đã dùng Next.js, Tailwind hoặc viết test với Testing Library.
                """,
            Location = "TP.HCM", SalaryMin = 18, SalaryMax = 30, Deadline = VietnamDateHelper.Today().AddDays(4), // sắp hết hạn
            EmploymentType = "Onsite",
            Status = JobStatus.Open, CreatedById = mentor.Id, CompanyId = fpt.Id, CreatedAt = DateTime.UtcNow.AddDays(-3)
        };
        var jDevOps = new Job
        {
            Title = "DevOps Engineer", Category = "DevOps", Level = "Senior",
            TechStack = "Linux, Docker, Kubernetes, CI/CD, Azure",
            Description = """
                Chịu trách nhiệm hạ tầng và quy trình triển khai cho các sản phẩm có hàng triệu người dùng.

                Công việc chính:
                - Vận hành cụm Kubernetes trên Azure; tối ưu chi phí và khả năng mở rộng.
                - Xây dựng và duy trì pipeline CI/CD (GitHub Actions) cho nhiều team phát triển.
                - Thiết lập giám sát, cảnh báo và quy trình xử lý sự cố (on-call luân phiên).
                - Viết Infrastructure as Code, chuẩn hóa môi trường dev/staging/production.

                Quyền lợi:
                - Làm việc hoàn toàn từ xa, có trợ cấp thiết bị và internet.
                - Phụ cấp on-call; ngân sách chứng chỉ Azure/CKA.
                - Thưởng cuối năm theo kết quả kinh doanh.
                """,
            Requirements = """
                - Tối thiểu 3 năm kinh nghiệm DevOps / SRE.
                - Thành thạo Linux, Docker, Kubernetes (Helm, Ingress, autoscaling).
                - Đã xây dựng pipeline CI/CD bằng GitHub Actions, GitLab CI hoặc Azure DevOps.
                - Có kinh nghiệm với Terraform hoặc Bicep; hiểu mạng và bảo mật cơ bản.
                - Ưu tiên: có chứng chỉ Azure Administrator hoặc CKA.
                """,
            Location = "Hà Nội", SalaryMin = 30, SalaryMax = 45, Deadline = VietnamDateHelper.Today().AddDays(25),
            EmploymentType = "Remote",
            Status = JobStatus.Open, CreatedById = mentor2.Id, CompanyId = vng.Id, CreatedAt = DateTime.UtcNow.AddDays(-2)
        };
        var jDataAi = new Job
        {
            Title = "Kỹ sư Data/AI (NLP)", Category = "Data/AI", Level = "Junior",
            TechStack = "Python, SQL, Machine Learning, LLM API",
            Description = """
                Tham gia xây dựng tính năng AI cho nền tảng tuyển dụng: đọc hiểu CV và đánh giá độ phù hợp với mô tả công việc.

                Công việc chính:
                - Xây dựng module trích xuất thông tin từ CV (kỹ năng, kinh nghiệm, học vấn) bằng NLP và LLM API.
                - Thiết kế prompt và bộ tiêu chí chấm độ phù hợp giữa CV và JD; đo độ chính xác trên tập dữ liệu thật.
                - Xử lý và làm sạch dữ liệu bằng Python, SQL; xây dựng pipeline đánh giá tự động.
                - Phối hợp team backend đưa mô hình vào sản phẩm, theo dõi chi phí và độ trễ.

                Quyền lợi:
                - Làm việc onsite tại TP.HCM, cấp máy cấu hình cao cho công việc AI.
                - Được tiếp cận dữ liệu và bài toán thực tế; mentor từ team Data/AI.
                - Ngân sách tham dự hội thảo AI trong nước.
                """,
            Requirements = """
                - Thành thạo Python (pandas, requests); viết được truy vấn SQL cơ bản đến trung bình.
                - Hiểu kiến thức Machine Learning cơ bản: phân loại, đánh giá mô hình, overfitting.
                - Đã dùng LLM API (OpenAI, Gemini, Claude...) và biết viết prompt có cấu trúc.
                - Có khả năng đọc tài liệu tiếng Anh chuyên ngành.
                - Ưu tiên: đã làm đồ án hoặc dự án cá nhân về NLP, có GitHub minh họa.
                """,
            Location = "TP.HCM", SalaryMin = 20, SalaryMax = 35, Deadline = VietnamDateHelper.Today().AddDays(15),
            EmploymentType = "Onsite",
            Status = JobStatus.Open, CreatedById = mentor.Id, CompanyId = fpt.Id, CreatedAt = DateTime.UtcNow.AddDays(-1)
        };
        var jIntern = new Job
        {
            Title = "Thực tập sinh Kiểm thử (QA)", Category = "QA", Level = "Intern",
            TechStack = "Manual Testing, SQL, Postman",
            Description = """
                Chương trình thực tập 3 tháng dành cho sinh viên muốn theo nghề kiểm thử phần mềm.

                Công việc chính:
                - Đọc tài liệu yêu cầu, viết test case và thực hiện kiểm thử chức năng cho web app.
                - Kiểm thử API bằng Postman; kiểm tra dữ liệu bằng các truy vấn SQL đơn giản.
                - Ghi nhận và theo dõi lỗi trên Jira; tham gia kiểm thử hồi quy trước mỗi lần phát hành.

                Quyền lợi:
                - Trợ cấp thực tập hằng tháng; có cơ hội trở thành nhân viên chính thức.
                - Được đào tạo quy trình kiểm thử và công cụ thực tế.
                """,
            Requirements = """
                - Sinh viên năm 3, năm 4 ngành CNTT hoặc liên quan.
                - Hiểu quy trình phát triển phần mềm cơ bản; cẩn thận, tỉ mỉ.
                - Biết SQL cơ bản; từng dùng Postman là một lợi thế.
                - Làm việc tối thiểu 4 buổi/tuần tại văn phòng Hà Nội.
                """,
            Location = "Hà Nội", SalaryMin = 3, SalaryMax = 5, Deadline = VietnamDateHelper.Today().AddDays(-2),
            EmploymentType = "Onsite",
            Status = JobStatus.Closed, CreatedById = mentor.Id, CompanyId = fpt.Id, CreatedAt = DateTime.UtcNow.AddDays(-10)
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
            CvContentType = "application/pdf", CvUploadedAt = DateTime.UtcNow,
            // P2-3: dữ liệu mẫu có sẵn sự đồng ý, nếu không thì mọi nút "Đánh giá bằng AI"
            // trên bản demo đều bị chặn và người xem tưởng chức năng hỏng.
            AiConsentAt = DateTime.UtcNow, AiConsentVersion = CandidateProfile.CurrentAiConsentVersion
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
            CvContentType = "application/pdf", CvUploadedAt = DateTime.UtcNow,
            // P2-3: dữ liệu mẫu có sẵn sự đồng ý, nếu không thì mọi nút "Đánh giá bằng AI"
            // trên bản demo đều bị chặn và người xem tưởng chức năng hỏng.
            AiConsentAt = DateTime.UtcNow, AiConsentVersion = CandidateProfile.CurrentAiConsentVersion
        };
        db.CandidateProfiles.AddRange(pLan, pKhoa);
        db.SaveChanges();

        // ===== Đơn ứng tuyển mẫu (ATS-10) + điểm AI mẫu (heuristic offline) =====
        Application MakeApp(Job job, CandidateProfile p, int daysAgo)
        {
            // Danh sách công nghệ đi vào ô có cấu trúc; phần mô tả tự do chỉ là văn bản
            // tham khảo. Trộn hai thứ vào một chuỗi chính là nguyên nhân khiến điểm
            // đối chiếu trước đây vô nghĩa.
            var e = GeminiAiService.HeuristicEvaluate(new AiEvaluationInput(
                CandidateText: $"{p.Skills} {p.Experience} {p.Education}",
                JobText: $"{job.Requirements} {job.Description}",
                CandidateTech: p.TechSkillTags,
                RequiredTech: job.TechStack));
            return new Application
            {
                JobId = job.Id, CandidateProfileId = p.Id,
                CvFileNameSnapshot = p.CvFileName!, CvDataSnapshot = p.CvData, CvContentTypeSnapshot = p.CvContentType,
                Status = ApplicationStatus.Submitted, AppliedAt = DateTime.UtcNow.AddDays(-daysAgo),
                AiScore = e.MatchPercent, AiStrengths = e.Strengths, AiMissing = e.Missing,
                AiRoadmap = e.Roadmap, AiSource = e.Source, AiScoredAt = DateTime.UtcNow
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
