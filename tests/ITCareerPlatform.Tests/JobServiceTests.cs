using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Xunit;

namespace ITCareerPlatform.Tests;

public class JobServiceTests
{
    [Fact]
    public void Create_SetsStatusOpen()
    {
        using var t = new TestDb();
        var mentor = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var svc = new JobService(t.Db);

        var job = svc.Create(new Job { Title = "Backend .NET", CreatedById = mentor.Id, Deadline = DateTime.Today.AddDays(5), Category = "Backend", Level = "Junior", TechStack = "C#,.NET" });

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

        svc.Update(job.Id, new Job { Title = "Đã sửa", Category = "DevOps", Level = "Senior", TechStack = "Docker", Deadline = DateTime.Today.AddDays(3) }, admin.Id);

        Assert.Equal("Đã sửa", t.NewContext().Jobs.Find(job.Id)!.Title);
    }
}
