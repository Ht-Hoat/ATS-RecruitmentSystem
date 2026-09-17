using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Xunit;

namespace ITCareerPlatform.Tests;

/// <summary>
/// P1-1: thực thể Công ty.
///
/// Điểm cốt lõi được khóa lại ở đây: CompanyId của một tin LUÔN đến từ tài khoản người tạo,
/// không bao giờ từ dữ liệu gửi lên. Nếu tin cậy giá trị gửi lên, một request tự tạo sẽ đăng
/// được tin đứng tên công ty khác — và trên màn hình sinh viên nó trông y hệt tin thật.
/// </summary>
public class CompanyServiceTests
{
    private static CompanyService NewSvc(TestDb t) => new(t.Db, new AuditService(t.Db));

    private static Company AddCompany(TestDb t, string name = "FPT Software")
    {
        var c = new Company { Name = name };
        t.Db.Companies.Add(c);
        t.Db.SaveChanges();
        return c;
    }

    private static User AddMentorWithCompany(TestDb t, Company c, string email = "m@itcp.vn")
    {
        var m = t.AddUser("M", email, Roles.MentorId);
        m.CompanyId = c.Id;
        t.Db.SaveChanges();
        return m;
    }

    private static Job NewJobInput(int createdBy, int companyIdFromRequest = 0) => new()
    {
        Title = "Backend .NET",
        Description = "Phát triển REST API bằng ASP.NET Core cho sản phẩm tuyển dụng IT.",
        Requirements = "Thành thạo C#, EF Core.",
        Category = "Backend", Level = "Junior", TechStack = "C#,.NET",
        EmploymentType = "Onsite",
        Deadline = VietnamDateHelper.Today().AddDays(10),
        CreatedById = createdBy,
        CompanyId = companyIdFromRequest
    };

    // ===== Công ty của tin đến từ người tạo =====

    [Fact]
    public void CreateJob_TakesCompanyFromTheCreator()
    {
        using var t = new TestDb();
        var c = AddCompany(t);
        var m = AddMentorWithCompany(t, c);

        var job = new JobService(t.Db).Create(NewJobInput(m.Id));

        Assert.Equal(c.Id, job.CompanyId);
    }

    /// <summary>
    /// Kịch bản tấn công cụ thể: request gửi kèm companyId của công ty khác. Kết quả phải
    /// KHÔNG đổi — tin vẫn đứng tên công ty của chính người đăng.
    /// </summary>
    [Fact]
    public void CreateJob_IgnoresCompanyIdSentInTheRequest()
    {
        using var t = new TestDb();
        var mine = AddCompany(t, "FPT Software");
        var someoneElse = AddCompany(t, "VNG Corporation");
        var m = AddMentorWithCompany(t, mine);

        var job = new JobService(t.Db).Create(NewJobInput(m.Id, companyIdFromRequest: someoneElse.Id));

        Assert.Equal(mine.Id, job.CompanyId);
        Assert.NotEqual(someoneElse.Id, job.CompanyId);
    }

    /// <summary>
    /// Chưa gán công ty thì phải nhận một câu tiếng Việt nói rõ phải làm gì tiếp — không
    /// để khóa ngoại nổ thành lỗi 500 sau khi người dùng đã gõ xong cả form.
    /// </summary>
    [Fact]
    public void CreateJob_WithoutCompany_FailsWithReadableMessage()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);

        var ex = Assert.Throws<ArgumentException>(() => new JobService(t.Db).Create(NewJobInput(m.Id)));

        Assert.Contains("chưa được gán vào công ty", ex.Message);
        Assert.Empty(t.NewContext().Jobs);
    }

    /// <summary>Sửa tin không đổi công ty: tin đã đứng tên ai thì giữ nguyên tên đó.</summary>
    [Fact]
    public void UpdateJob_KeepsTheOriginalCompany()
    {
        using var t = new TestDb();
        var mine = AddCompany(t, "FPT Software");
        var other = AddCompany(t, "VNG Corporation");
        var m = AddMentorWithCompany(t, mine);
        var svc = new JobService(t.Db);
        var job = svc.Create(NewJobInput(m.Id));

        var input = NewJobInput(m.Id, companyIdFromRequest: other.Id);
        input.Title = "Backend .NET (đã sửa)";
        svc.Update(job.Id, input, m.Id);

        var after = t.NewContext().Jobs.Find(job.Id)!;
        Assert.Equal("Backend .NET (đã sửa)", after.Title);
        Assert.Equal(mine.Id, after.CompanyId);
    }

    // ===== Validate =====

    [Theory]
    [InlineData("http://congty.vn")]          // http, không phải https
    [InlineData("javascript:alert(1)")]       // sẽ thành href trên trang sinh viên
    [InlineData("congty.vn")]                 // thiếu scheme
    public void Save_InvalidWebsite_IsRejected(string website)
    {
        using var t = new TestDb();
        var admin = t.AddUser("Admin", "admin@itcp.vn", Roles.AdminId);

        var ex = Assert.Throws<ArgumentException>(() =>
            NewSvc(t).Save(0, new Company { Name = "Công ty A", Website = website }, admin.Id));

        Assert.Contains("https://", ex.Message);
        Assert.Empty(t.NewContext().Companies);
    }

    [Fact]
    public void Save_EmptyWebsite_IsAllowed()
    {
        using var t = new TestDb();
        var admin = t.AddUser("Admin", "admin@itcp.vn", Roles.AdminId);

        var c = NewSvc(t).Save(0, new Company { Name = "Công ty A" }, admin.Id);

        Assert.True(c.Id > 0);
    }

    [Fact]
    public void Save_EmptyName_IsRejected()
    {
        using var t = new TestDb();
        var admin = t.AddUser("Admin", "admin@itcp.vn", Roles.AdminId);

        Assert.Throws<ArgumentException>(() => NewSvc(t).Save(0, new Company { Name = "   " }, admin.Id));
    }

    /// <summary>
    /// Hai công ty cùng tên là hai dòng không phân biệt được trên màn hình gán công ty cho
    /// tài khoản — và gán nhầm thì tin đứng tên sai mà không ai nhận ra.
    /// </summary>
    [Fact]
    public void Save_DuplicateName_IsRejected()
    {
        using var t = new TestDb();
        var admin = t.AddUser("Admin", "admin@itcp.vn", Roles.AdminId);
        var svc = NewSvc(t);
        svc.Save(0, new Company { Name = "FPT Software" }, admin.Id);

        var ex = Assert.Throws<ArgumentException>(() =>
            svc.Save(0, new Company { Name = "FPT Software" }, admin.Id));

        Assert.Contains("đã có công ty khác", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Sửa chính công ty đó mà giữ nguyên tên thì không phải là trùng tên.</summary>
    [Fact]
    public void Save_SameNameOnItself_IsAllowed()
    {
        using var t = new TestDb();
        var admin = t.AddUser("Admin", "admin@itcp.vn", Roles.AdminId);
        var svc = NewSvc(t);
        var c = svc.Save(0, new Company { Name = "FPT Software" }, admin.Id);

        var updated = svc.Save(c.Id, new Company { Name = "FPT Software", Address = "Hà Nội" }, admin.Id);

        Assert.Equal(c.Id, updated.Id);
        Assert.Equal("Hà Nội", updated.Address);
    }

    // ===== Gán công ty cho tài khoản =====

    [Fact]
    public void AssignToUser_SetsAndClearsCompany()
    {
        using var t = new TestDb();
        var admin = t.AddUser("Admin", "admin@itcp.vn", Roles.AdminId);
        var c = AddCompany(t);
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var svc = NewSvc(t);

        svc.AssignToUser(m.Id, c.Id, admin.Id);
        Assert.Equal(c.Id, t.NewContext().Users.Find(m.Id)!.CompanyId);

        svc.AssignToUser(m.Id, null, admin.Id);
        Assert.Null(t.NewContext().Users.Find(m.Id)!.CompanyId);
    }

    [Fact]
    public void AssignToUser_StudentAccount_IsRejected()
    {
        using var t = new TestDb();
        var admin = t.AddUser("Admin", "admin@itcp.vn", Roles.AdminId);
        var c = AddCompany(t);
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);

        var ex = Assert.Throws<ArgumentException>(() => NewSvc(t).AssignToUser(sv.Id, c.Id, admin.Id));

        Assert.Contains("Sinh viên", ex.Message);
        Assert.Null(t.NewContext().Users.Find(sv.Id)!.CompanyId);
    }

    [Fact]
    public void AssignToUser_UnknownCompany_IsRejected()
    {
        using var t = new TestDb();
        var admin = t.AddUser("Admin", "admin@itcp.vn", Roles.AdminId);
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);

        Assert.Throws<ArgumentException>(() => NewSvc(t).AssignToUser(m.Id, 9999, admin.Id));
    }

    [Fact]
    public void GetByUserId_ReturnsNull_WhenNotAssigned()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);

        Assert.Null(NewSvc(t).GetByUserId(m.Id));
    }

    // ===== Đường hiển thị mang theo công ty =====

    /// <summary>
    /// Trang chi tiết tin tồn tại để sinh viên biết mình ứng tuyển cho AI — nên công ty phải
    /// đi kèm trong cùng một lần đọc, không để trang phải hỏi thêm một vòng nữa.
    /// </summary>
    [Fact]
    public void GetVisibleForCandidate_CarriesTheCompany()
    {
        using var t = new TestDb();
        var c = AddCompany(t);
        var m = AddMentorWithCompany(t, c);
        var job = new JobService(t.Db).Create(NewJobInput(m.Id));

        var visible = new JobService(t.NewContext()).GetVisibleForCandidate(job.Id)!;

        Assert.NotNull(visible.Company);
        Assert.Equal("FPT Software", visible.Company!.Name);
    }

    [Fact]
    public void Filter_CarriesTheCompany_ForTheJobCards()
    {
        using var t = new TestDb();
        var c = AddCompany(t);
        var m = AddMentorWithCompany(t, c);
        new JobService(t.Db).Create(NewJobInput(m.Id));

        var list = new JobService(t.NewContext()).Filter(null, null, null, "new");

        Assert.Equal("FPT Software", Assert.Single(list).Company!.Name);
    }

    [Fact]
    public void CountJobsPerCompany_CountsOnlyItsOwnJobs()
    {
        using var t = new TestDb();
        var fpt = AddCompany(t, "FPT Software");
        var vng = AddCompany(t, "VNG Corporation");
        var m1 = AddMentorWithCompany(t, fpt, "m1@itcp.vn");
        var m2 = AddMentorWithCompany(t, vng, "m2@itcp.vn");
        var svc = new JobService(t.Db);
        svc.Create(NewJobInput(m1.Id));
        var second = NewJobInput(m1.Id); second.Title = "Tin thứ hai";
        svc.Create(second);
        svc.Create(NewJobInput(m2.Id));

        var counts = NewSvc(t).CountJobsPerCompany();

        Assert.Equal(2, counts[fpt.Id]);
        Assert.Equal(1, counts[vng.Id]);
    }
}
