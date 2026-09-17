using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Xunit;

namespace ITCareerPlatform.Tests;

public class JobServiceTests
{
    // N2.C: hình thức làm việc phải được kiểm ở tầng service, không chỉ ở thẻ <select>.
    [Fact]
    public void Create_InvalidEmploymentType_Throws()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var svc = new JobService(t.Db);

        var job = new Job
        {
            Title = "Backend .NET", Category = "Backend", Level = "Junior",
            EmploymentType = "TuChoiVe",          // không thuộc Job.EmploymentTypes
            CreatedById = m.Id, Deadline = VietnamDateHelper.Today().AddDays(10)
        };

        var ex = Assert.Throws<ArgumentException>(() => svc.Create(job));
        Assert.Contains("Hình thức làm việc", ex.Message);
    }

    [Fact]
    public void Create_DefaultEmploymentType_IsAccepted()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var svc = new JobService(t.Db);

        var job = svc.Create(new Job
        {
            Title = "Backend .NET", Category = "Backend", Level = "Junior",
            CreatedById = m.Id, Deadline = VietnamDateHelper.Today().AddDays(10)
        });

        Assert.Equal("Onsite", job.EmploymentType);
    }

    [Fact]
    public void Create_SetsStatusOpen()
    {
        using var t = new TestDb();
        var mentor = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var svc = new JobService(t.Db);

        var job = svc.Create(new Job { Title = "Backend .NET", CreatedById = mentor.Id, Deadline = VietnamDateHelper.Today().AddDays(5), Category = "Backend", Level = "Junior", TechStack = "C#,.NET" });

        Assert.Equal("Open", job.Status);
        Assert.True(job.Id > 0);
    }

    [Fact]
    public void Create_SalaryMaxLessThanMin_Throws()
    {
        using var t = new TestDb();
        var mentor = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var svc = new JobService(t.Db);

        Assert.Throws<ArgumentException>(() =>
            svc.Create(new Job { Title = "X", CreatedById = mentor.Id, SalaryMin = 20, SalaryMax = 10 }));
    }

    // ATS-07: lọc theo Category
    [Fact]
    public void Filter_ByCategory_ReturnsOnlyMatching()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        t.AddJob(m.Id, "BE", "Backend", "C#,.NET");
        t.AddJob(m.Id, "FE", "Frontend", "React,JS");
        var svc = new JobService(t.Db);

        var result = svc.Filter("Frontend", null, null, "new");

        Assert.Single(result);
        Assert.Equal("FE", result[0].Title);
    }

    // ATS-07: tìm TechStack không phân biệt hoa thường
    [Fact]
    public void Filter_ByTechStack_CaseInsensitive()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        t.AddJob(m.Id, "BE", "Backend", "C#,.NET,SQL Server");
        t.AddJob(m.Id, "FE", "Frontend", "React,TypeScript");
        var svc = new JobService(t.Db);

        var result = svc.Filter(null, "react", null, "new");

        Assert.Single(result);
        Assert.Equal("FE", result[0].Title);
    }

    [Fact]
    public void Filter_OnlyOpenJobs()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        t.AddJob(m.Id, "Open1", status: "Open");
        t.AddJob(m.Id, "Closed1", status: "Closed");
        var svc = new JobService(t.Db);

        var result = svc.Filter(null, null, null, "new");

        Assert.Single(result);
        Assert.Equal("Open1", result[0].Title);
    }

    // ATS-05.3: không sửa tin đã Closed
    [Fact]
    public void Update_ClosedJob_Throws()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var job = t.AddJob(m.Id, status: "Closed");
        var svc = new JobService(t.Db);

        Assert.Throws<InvalidOperationException>(() =>
            svc.Update(job.Id, new Job { Title = "New" }, m.Id));
    }

    // ATS-05.2: người không phải chủ tin và không phải Admin -> chặn
    [Fact]
    public void Update_NotOwnerNotAdmin_Throws()
    {
        using var t = new TestDb();
        var owner = t.AddUser("Owner", "o@itcp.vn", Roles.MentorId);
        var other = t.AddUser("Other", "x@itcp.vn", Roles.MentorId);
        var job = t.AddJob(owner.Id);
        var svc = new JobService(t.Db);

        Assert.Throws<UnauthorizedAccessException>(() =>
            svc.Update(job.Id, new Job { Title = "Hack" }, other.Id));
    }

    [Fact]
    public void Update_ByAdmin_Succeeds()
    {
        using var t = new TestDb();
        var owner = t.AddUser("Owner", "o@itcp.vn", Roles.MentorId);
        var admin = t.AddUser("Admin", "a@itcp.vn", Roles.AdminId);
        var job = t.AddJob(owner.Id);
        var svc = new JobService(t.Db);

        svc.Update(job.Id, new Job { Title = "Đã sửa", Category = "DevOps", Level = "Senior", TechStack = "Docker", Deadline = VietnamDateHelper.Today().AddDays(3) }, admin.Id);

        Assert.Equal("Đã sửa", t.NewContext().Jobs.Find(job.Id)!.Title);
    }

    // =====================================================================
    // N2.C TESTS: EmploymentType, Salary, Location Case-Insensitive, Combined
    // =====================================================================

    [Fact]
    public void Filter_ByEmploymentType_MatchesExact()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        t.AddJob(m.Id, "Job Onsite", employmentType: "Onsite");
        t.AddJob(m.Id, "Job Remote", employmentType: "Remote");
        t.AddJob(m.Id, "Job Hybrid", employmentType: "Hybrid");
        var svc = new JobService(t.Db);

        var onsite = svc.Filter(null, null, null, "new", employmentType: "Onsite");
        var remote = svc.Filter(null, null, null, "new", employmentType: "Remote");
        var hybrid = svc.Filter(null, null, null, "new", employmentType: "Hybrid");

        Assert.Single(onsite); Assert.Equal("Job Onsite", onsite[0].Title);
        Assert.Single(remote); Assert.Equal("Job Remote", remote[0].Title);
        Assert.Single(hybrid); Assert.Equal("Job Hybrid", hybrid[0].Title);
    }

    [Fact]
    public void Filter_ByMinSalary_MatchesGreaterOrEqual()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        t.AddJob(m.Id, "Job Low", salaryMax: 15);
        t.AddJob(m.Id, "Job Mid", salaryMax: 25);
        t.AddJob(m.Id, "Job High", salaryMax: 40);
        var svc = new JobService(t.Db);

        var result = svc.Filter(null, null, null, "new", minSalary: 25);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, j => j.Title == "Job Mid");
        Assert.Contains(result, j => j.Title == "Job High");
        Assert.DoesNotContain(result, j => j.Title == "Job Low");
    }

    [Theory]
    [InlineData("Hà Nội")]
    [InlineData("hà nội")]
    [InlineData("HÀ NỘI")]
    [InlineData("hÀ NộI")]
    public void Filter_ByLocation_CaseInsensitive(string locationKeyword)
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        t.AddJob(m.Id, "Job HaNoi", location: "Hà Nội");
        t.AddJob(m.Id, "Job HCM", location: "TP.HCM");
        var svc = new JobService(t.Db);

        var result = svc.Filter(null, null, null, "new", location: locationKeyword);

        Assert.Single(result);
        Assert.Equal("Job HaNoi", result[0].Title);
    }

    [Theory]
    [InlineData("HÀ NỘI", "hà nội")]
    [InlineData("hà nội", "HÀ NỘI")]
    [InlineData("hÀ NộI", "Hà Nội")]
    [InlineData("Hà Nội", "hÀ NộI")]
    public void Filter_ByLocation_VaryingDatabaseAndQueryCasing(string dbLocation, string queryLocation)
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        t.AddJob(m.Id, "Job HaNoi", location: dbLocation);
        var svc = new JobService(t.Db);

        var result = svc.Filter(null, null, null, "new", location: queryLocation);

        Assert.Single(result);
        Assert.Equal("Job HaNoi", result[0].Title);
    }

    [Fact]
    public void Filter_CombinedFilters_MatchesAllCriteria()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        // Tin thỏa mãn toàn bộ tiêu chí
        t.AddJob(m.Id, "Target Job", category: "Backend", techStack: "C#,.NET Core", level: "Junior",
            employmentType: "Remote", location: "Hà Nội", salaryMax: 30);

        // Tin khác category
        t.AddJob(m.Id, "Other Cat", category: "Frontend", techStack: "C#,.NET Core", level: "Junior",
            employmentType: "Remote", location: "Hà Nội", salaryMax: 30);

        // Tin không đủ lương
        t.AddJob(m.Id, "Low Salary", category: "Backend", techStack: "C#,.NET Core", level: "Junior",
            employmentType: "Remote", location: "Hà Nội", salaryMax: 15);

        // Tin khác hình thức làm việc
        t.AddJob(m.Id, "Onsite Job", category: "Backend", techStack: "C#,.NET Core", level: "Junior",
            employmentType: "Onsite", location: "Hà Nội", salaryMax: 30);

        var svc = new JobService(t.Db);
        var result = svc.Filter("Backend", "C#", "Junior", "new",
            minSalary: 20, employmentType: "Remote", location: "hà nội");

        Assert.Single(result);
        Assert.Equal("Target Job", result[0].Title);
    }
}
