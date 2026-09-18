using System.Net;
using System.Text.RegularExpressions;
using ITCareerPlatform.Data;
using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ITCareerPlatform.Tests;

/// <summary>
/// Dựng app THẬT (Program.cs, middleware, endpoint, trang Razor) trên SQLite trong bộ nhớ.
///
/// Môi trường Development để SeedData nạp sẵn tài khoản mẫu (mật khẩu "123456"). Tiến trình
/// nền gửi email bị gỡ ra: nó chạy trên một luồng khác và sẽ dùng chung kết nối SQLite với
/// request đang chạy.
/// </summary>
public sealed class AppFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _conn = new("DataSource=:memory:");
    private readonly string _cvRoot = Path.Combine(Path.GetTempPath(), "itcp-http-test", Guid.NewGuid().ToString("N"));

    public AppFactory()
    {
        _conn.Open();
        _conn.CreateFunction("lower", (string? s) => s?.ToLower());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("CvStorage:RootPath", _cvRoot);
        builder.ConfigureTestServices(s =>
        {
            s.RemoveAll<DbContextOptions<AppDbContext>>();
            s.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            s.AddDbContext<AppDbContext>(o => o.UseSqlite(_conn));

            foreach (var d in s.Where(d => d.ServiceType == typeof(IHostedService)
                                           && d.ImplementationType == typeof(OutboxSender)).ToList())
                s.Remove(d);
        });
    }

    // ===== Helper dùng chung cho các lớp test HTTP =====

    public const string SeedPassword = "123456";
    private static readonly Regex TokenInput = new(@"name=""__RequestVerificationToken""[^>]*value=""(?<t>[^""]+)""");

    /// <summary>
    /// Đăng nhập bằng tài khoản mẫu. Mỗi lần tốn một lượt của giới hạn tốc độ (10 lượt/phút/IP),
    /// tính theo TỪNG app — lớp test nào đăng nhập nhiều thì dùng AppFactory riêng.
    /// </summary>
    public async Task<HttpClient> LoginAs(string email)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var res = await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = email, ["password"] = SeedPassword
        }));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal("/", res.Headers.Location?.OriginalString);
        return client;
    }

    /// <summary>Token chống giả mạo có trong trang; test đỏ ngay nếu trang không có ô token.</summary>
    public static string TokenIn(string html)
    {
        var m = TokenInput.Match(html);
        Assert.True(m.Success, "Trang không có ô token chống giả mạo.");
        return WebUtility.HtmlDecode(m.Groups["t"].Value);
    }

    public static async Task<string> TokenFrom(HttpClient client, string page) =>
        TokenIn(await client.GetStringAsync(page));

    public static FormUrlEncodedContent Form(string? token, params (string Key, string Value)[] fields)
    {
        var all = fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)).ToList();
        if (token is not null) all.Add(new("__RequestVerificationToken", token));
        return new FormUrlEncodedContent(all);
    }

    public T Query<T>(Func<AppDbContext, T> read)
    {
        using var scope = Services.CreateScope();
        return read(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        _conn.Dispose();
        try { if (Directory.Exists(_cvRoot)) Directory.Delete(_cvRoot, recursive: true); }
        catch (IOException) { }
    }
}

public class HttpEndpointTests(AppFactory app) : IClassFixture<AppFactory>
{
    // Cả lớp dùng chung một app, nên tổng số lần đăng nhập của lớp phải dưới 10 (giới hạn tốc độ).
    private Task<HttpClient> LoginAs(string email) => app.LoginAs(email);
    private static Task<string> TokenFrom(HttpClient client, string page) => AppFactory.TokenFrom(client, page);
    private static FormUrlEncodedContent Form(string? token, params (string Key, string Value)[] fields) =>
        AppFactory.Form(token, fields);

    private int JobId(string title) => app.Query(db => db.Jobs.Single(j => j.Title == title).Id);

    // ===== P2-3: antiforgery =====

    [Fact]
    public async Task Post_WithoutAntiforgeryToken_IsRejected_AndChangesNothing()
    {
        var mentor = await LoginAs("mentor@itcp.vn");
        var jobId = JobId("Lập trình viên Backend .NET");

        var res = await mentor.PostAsync($"/jobs/{jobId}/close", Form(token: null));

        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal("/error?reason=antiforgery", res.Headers.Location?.OriginalString);
        Assert.Equal(JobStatus.Open, app.Query(db => db.Jobs.Find(jobId)!.Status));
    }

    [Fact]
    public async Task Post_WithTokenFromThePage_GoesThrough()
    {
        var mentor = await LoginAs("mentor@itcp.vn");
        var jobId = JobId("Kỹ sư Data/AI (NLP)");
        var token = await TokenFrom(mentor, "/jobs");

        var res = await mentor.PostAsync($"/jobs/{jobId}/close", Form(token));

        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal("/jobs", res.Headers.Location?.OriginalString);
        Assert.Equal(JobStatus.Closed, app.Query(db => db.Jobs.Find(jobId)!.Status));
    }

    // ===== P2-1: xuất CSV — quyền chỉ kiểm ở endpoint =====

    [Fact]
    public async Task Export_ByAnotherMentor_IsDenied_ButOwnerGetsCsv()
    {
        var jobId = JobId("Lập trình viên Backend .NET");   // tin của mentor@itcp.vn

        var intruder = await LoginAs("mentor2@itcp.vn");
        var denied = await intruder.GetAsync($"/jobs/{jobId}/applicants/export");
        Assert.NotEqual(HttpStatusCode.OK, denied.StatusCode);
        Assert.Contains("/denied", denied.Headers.Location?.OriginalString ?? "");

        var owner = await LoginAs("mentor@itcp.vn");
        var ok = await owner.GetAsync($"/jobs/{jobId}/applicants/export");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.StartsWith("text/csv", ok.Content.Headers.ContentType?.ToString());
    }

    // ===== Trang chi tiết tin =====

    /// <summary>
    /// Trang chi tiết từng hiển thị bản tin nạp bằng Find (không kèm Company), nên khối giới
    /// thiệu công ty không bao giờ hiện. Kiểm cả JD và yêu cầu nằm trên trang.
    /// </summary>
    [Fact]
    public async Task JobDetail_ShowsCompany_JD_AndRequirements()
    {
        var student = await LoginAs("khoa@itcp.vn");
        var job = app.Query(db => db.Jobs.Include(j => j.Company).Single(j => j.Title == "DevOps Engineer"));

        // So trên văn bản đã giải mã: Razor mã hóa ký tự tiếng Việt thành thực thể HTML.
        var html = WebUtility.HtmlDecode(await student.GetStringAsync($"/positions/{job.Id}"));

        Assert.Contains($"🏢 {job.Company!.Name}", html);
        Assert.Contains("Mô tả công việc (JD)", html);
        Assert.Contains(job.Description.Split('\n')[0].Trim(), html);
        Assert.Contains("Yêu cầu ứng viên", html);
    }

    // ===== Đăng xuất =====

    /// <summary>
    /// Nút Đăng xuất từng có ô token nằm NGOÀI thẻ form, nên mọi lần bấm đều rơi vào trang
    /// "Phiên làm việc đã hết hạn" và không đăng xuất được. Bấm thật: lấy token từ trang,
    /// gửi đúng form đó, rồi kiểm là phiên đã hết.
    /// </summary>
    [Fact]
    public async Task Logout_FromTheAccountMenu_SignsOut()
    {
        var student = await LoginAs("lan@itcp.vn");
        var token = await TokenFrom(student, "/");

        var res = await student.PostAsync("/account/logout", Form(token));

        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal("/login", res.Headers.Location?.OriginalString);
        var after = await student.GetAsync("/profile");
        Assert.Contains("/login", after.Headers.Location?.OriginalString ?? "");
    }

    // ===== Admin chỉ xem phần tuyển dụng =====

    [Fact]
    public async Task Admin_SeesRecruitmentReadOnly_AndIsBlockedFromEveryAction()
    {
        var admin = await LoginAs("admin@itcp.vn");
        var jobId = JobId("Lập trình viên Backend .NET");
        var appId = app.Query(db => db.Applications.First(a => a.JobId == jobId).Id);

        // Xem được: danh sách tin, danh sách ứng viên, chi tiết đơn — nhưng không có nút thao tác.
        var jobs = await admin.GetStringAsync("/jobs");
        Assert.DoesNotContain("href=\"/jobs/new\"", jobs);
        Assert.DoesNotContain($"/jobs/{jobId}/close", jobs);
        Assert.DoesNotContain($"/jobs/edit/{jobId}", jobs);

        var applicants = await admin.GetAsync($"/jobs/{jobId}/applicants");
        Assert.Equal(HttpStatusCode.OK, applicants.StatusCode);
        var applicantsHtml = await applicants.Content.ReadAsStringAsync();
        Assert.Contains("Chế độ chỉ xem", applicantsHtml);
        Assert.DoesNotContain("bulk-status", applicantsHtml);
        Assert.DoesNotContain("/applicants/export", applicantsHtml);

        var detail = await admin.GetAsync($"/applications/{appId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var detailHtml = await detail.Content.ReadAsStringAsync();
        foreach (var action in new[] { "/ai-evaluate", "/status\"" })
            Assert.DoesNotContain(action, detailHtml);

        // Trang tạo tin: không vào được.
        var form = await admin.GetAsync("/jobs/new");
        Assert.NotEqual(HttpStatusCode.OK, form.StatusCode);
        Assert.Contains("/denied", form.Headers.Location?.OriginalString ?? "");

        // Request tự tạo, có token hợp lệ: vẫn bị chặn ở endpoint, và không có gì thay đổi.
        var token = await TokenFrom(admin, "/users");
        foreach (var (path, fields) in new (string, (string, string)[])[]
                 {
                     ($"/jobs/{jobId}/close", []),
                     ($"/applications/{appId}/status", [("status", ApplicationStatus.Rejected)]),
                     ($"/applications/{appId}/ai-evaluate", []),
                     ($"/jobs/{jobId}/applicants/bulk-status", [("status", ApplicationStatus.Rejected), ("applicationId", appId.ToString())]),
                 })
        {
            var res = await admin.PostAsync(path, Form(token, fields));
            Assert.True(res.StatusCode == HttpStatusCode.Redirect && (res.Headers.Location?.OriginalString ?? "").Contains("/denied"),
                $"POST {path} trả {(int)res.StatusCode} {res.Headers.Location} — lẽ ra phải bị chặn.");
        }

        var export = await admin.GetAsync($"/jobs/{jobId}/applicants/export");
        Assert.NotEqual(HttpStatusCode.OK, export.StatusCode);

        var after = app.Query(db => new
        {
            Job = db.Jobs.Find(jobId)!.Status,
            App = db.Applications.Find(appId)!
        });
        Assert.Equal(JobStatus.Open, after.Job);
        Assert.NotEqual(ApplicationStatus.Rejected, after.App.Status);
    }

    // ===== P1-1: hai endpoint công ty từng bị xóa nhầm =====

    [Fact]
    public async Task CompanyPage_SaveForm_ReachesTheService()
    {
        var admin = await LoginAs("admin@itcp.vn");
        var token = await TokenFrom(admin, "/companies");

        var res = await admin.PostAsync("/companies/save", Form(token,
            ("id", "0"), ("name", "Công ty Kiểm thử HTTP"), ("website", ""), ("address", ""), ("description", "")));

        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.StartsWith("/companies?msg=", res.Headers.Location?.OriginalString);
        Assert.True(app.Query(db => db.Companies.Any(c => c.Name == "Công ty Kiểm thử HTTP")));
    }

    // ===== P0-3: mật khẩu tạm không đi qua thanh địa chỉ =====

    [Fact]
    public async Task ResetPassword_ShowsTempPasswordOnce_AndNeverPutsItInTheUrl()
    {
        var admin = await LoginAs("admin@itcp.vn");
        var target = app.Query(db => db.Users.Single(u => u.Email == "khoa@itcp.vn"));
        var token = await TokenFrom(admin, "/users");

        var res = await admin.PostAsync($"/users/{target.Id}/reset-password", Form(token));

        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal("/users?tempPw=1", res.Headers.Location?.OriginalString);

        // Lần mở đầu tiên hiện mật khẩu — và đó đúng là mật khẩu mới của tài khoản.
        var firstView = await admin.GetStringAsync("/users?tempPw=1");
        var shown = Regex.Match(firstView, @"letter-spacing: 1px;"">(?<pw>[^<]+)</code>");
        Assert.True(shown.Success, "Trang không hiện mật khẩu tạm ở lần mở đầu tiên.");
        var pw = shown.Groups["pw"].Value;
        var hash = app.Query(db => db.Users.Single(u => u.Id == target.Id).PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify(pw, hash));

        // Không nằm trong Location, cũng không nằm dạng rõ trong cookie.
        Assert.DoesNotContain(pw, res.Headers.Location!.OriginalString);
        Assert.DoesNotContain(pw, string.Join(";", res.Headers.TryGetValues("Set-Cookie", out var c) ? c : []));

        // Tải lại trang: mật khẩu đã biến mất.
        var secondView = await admin.GetStringAsync("/users?tempPw=1");
        Assert.DoesNotContain(pw, secondView);
        Assert.Contains("chỉ hiện một lần", secondView);
    }
}
