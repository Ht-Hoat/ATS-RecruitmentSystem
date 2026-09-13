using ITCareerPlatform.Components;
using ITCareerPlatform.Data;
using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// ---------- Blazor (server-rendered) + trạng thái đăng nhập ----------
builder.Services.AddRazorComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuthenticationStateProvider, HttpContextAuthStateProvider>();

// ---------- CSDL: SQL Server qua EF Core ----------
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ---------- Xác thực Cookie + phân quyền theo Role ----------
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.AccessDeniedPath = "/denied";
        o.Cookie.Name = "ITCP.Auth";
    });
builder.Services.AddAuthorization();

// ---------- HttpClient cho Gemini (ATS-13) ----------
builder.Services.AddHttpClient("gemini", c => c.Timeout = TimeSpan.FromSeconds(30));

// ---------- Tầng nghiệp vụ ----------
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IJobService, JobService>();
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IApplicationService, ApplicationService>();
builder.Services.AddScoped<IAiService, GeminiAiService>();

var app = builder.Build();

// ---------- Tạo CSDL + seed khi khởi động ----------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.GetMigrations().Any()) db.Database.Migrate();
    else db.Database.EnsureCreated();
    SeedData.Initialize(db);
}

// #11: ngoài môi trường Development, lỗi chưa bắt được sẽ hiển thị trang /error thân thiện
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/error");

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Helper lấy Id người đang đăng nhập
static int CurrentUserId(HttpContext ctx) =>
    int.TryParse(ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;

static bool IsAdmin(HttpContext ctx) => ctx.User.IsInRole(Roles.Admin);

static string Enc(string s) => Uri.EscapeDataString(s);

// ============================ AUTH (ATS-03, EXT-01) ============================
app.MapPost("/account/login", async (HttpContext ctx, IAuthService auth) =>
{
    var f = await ctx.Request.ReadFormAsync();
    var emailVal = f["email"].ToString();
    var user = auth.Validate(emailVal, f["password"].ToString());
    if (user is null) return Results.Redirect("/login?error=1&email=" + Enc(emailVal));

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new(ClaimTypes.Name, user.FullName),
        new(ClaimTypes.Email, user.Email),
        new(ClaimTypes.Role, user.Role?.RoleName ?? Roles.Student),
    };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.Redirect("/");
}).DisableAntiforgery();

app.MapPost("/account/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).DisableAntiforgery();

app.MapPost("/account/register", async (HttpContext ctx, IUserService svc) =>
{
    var f = await ctx.Request.ReadFormAsync();
    if (f["password"].ToString() != f["confirmPassword"].ToString())
        return Results.Redirect("/register?error=" + Enc("Mật khẩu xác nhận không khớp."));
    if (!svc.Register(f["fullName"].ToString(), f["email"].ToString(), f["password"].ToString(), out var error))
        return Results.Redirect("/register?error=" + Enc(error));
    return Results.Redirect("/login?registered=1");
}).DisableAntiforgery();

// ============================ USERS (ATS-01, ATS-02) ============================
app.MapPost("/users/create", async (HttpContext ctx, IUserService svc) =>
{
    var f = await ctx.Request.ReadFormAsync();
    var pw = string.IsNullOrEmpty(f["password"]) ? "123456" : f["password"].ToString();
    svc.Create(f["fullName"].ToString(), f["email"].ToString(), pw, int.Parse(f["roleId"]!));
    return Results.Redirect("/users");
}).RequireAuthorization(p => p.RequireRole(Roles.Admin)).DisableAntiforgery();

app.MapPost("/users/{id:int}/toggle-lock", (int id, HttpContext ctx, IUserService svc) =>
{
    if (id == CurrentUserId(ctx)) return Results.Redirect("/users?err=" + Enc("Không thể tự khóa tài khoản của mình."));
    svc.ToggleLock(id);
    return Results.Redirect("/users");
}).RequireAuthorization(p => p.RequireRole(Roles.Admin)).DisableAntiforgery();

app.MapPost("/users/{id:int}/change-role", async (int id, HttpContext ctx, IUserService svc) =>
{
    if (id == CurrentUserId(ctx)) return Results.Redirect("/users?err=" + Enc("Không thể tự đổi vai trò của mình."));
    var f = await ctx.Request.ReadFormAsync();
    svc.ChangeRole(id, int.Parse(f["roleId"]!), CurrentUserId(ctx));
    return Results.Redirect("/users");
}).RequireAuthorization(p => p.RequireRole(Roles.Admin)).DisableAntiforgery();

// ============================ JOBS (ATS-04, 05, 06) ============================
static Job ReadJobForm(IFormCollection f, int actor)
{
    var rawEmploymentType = f["employmentType"].ToString();
    var safeEmploymentType = Job.EmploymentTypes.Contains(rawEmploymentType) ? rawEmploymentType : "Onsite";

    return new()
    {
        Title = f["title"].ToString(),
        Description = f["description"].ToString(),
        Requirements = f["requirements"].ToString(),
        Location = f["location"].ToString(),
        SalaryMin = decimal.TryParse(f["salaryMin"], out var mn) ? mn : 0,
        SalaryMax = decimal.TryParse(f["salaryMax"], out var mx) ? mx : 0,
        Deadline = DateTime.TryParse(f["deadline"], out var d) ? d : DateTime.Today.AddMonths(1),
        Category = string.IsNullOrEmpty(f["category"]) ? "Khác" : f["category"].ToString(),
        TechStack = f["techStack"].ToString(),
        Level = string.IsNullOrEmpty(f["level"]) ? "Junior" : f["level"].ToString(),
        EmploymentType = safeEmploymentType,
        CreatedById = actor
    };
}

app.MapPost("/jobs/create", async (HttpContext ctx, IJobService svc) =>
{
    var f = await ctx.Request.ReadFormAsync();
    try { svc.Create(ReadJobForm(f, CurrentUserId(ctx))); return Results.Redirect("/jobs"); }
    catch (Exception ex) { return Results.Redirect("/jobs/new?error=" + Enc(ex.Message)); }
}).RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Mentor)).DisableAntiforgery();

app.MapPost("/jobs/{id:int}/update", async (int id, HttpContext ctx, IJobService svc) =>
{
    var f = await ctx.Request.ReadFormAsync();
    try { svc.Update(id, ReadJobForm(f, CurrentUserId(ctx)), CurrentUserId(ctx)); return Results.Redirect("/jobs"); }
    catch (Exception ex) { return Results.Redirect($"/jobs/edit/{id}?error=" + Enc(ex.Message)); }
}).RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Mentor)).DisableAntiforgery();

app.MapPost("/jobs/{id:int}/close", (int id, IJobService svc) => { svc.Close(id); return Results.Redirect("/jobs"); })
   .RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Mentor)).DisableAntiforgery();
app.MapPost("/jobs/{id:int}/reopen", (int id, IJobService svc) => { svc.Reopen(id); return Results.Redirect("/jobs"); })
   .RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Mentor)).DisableAntiforgery();

// ============================ PROFILE (ATS-08, ATS-09) ============================
app.MapPost("/profile/save", async (HttpContext ctx, IProfileService svc) =>
{
    var uid = CurrentUserId(ctx);
    if (uid == 0) return Results.Redirect("/login");
    var f = await ctx.Request.ReadFormAsync();
    try
    {
        svc.Save(uid, new CandidateProfile
        {
            FullName = f["fullName"].ToString(),
            Email = f["email"].ToString(),
            Phone = f["phone"].ToString(),
            DateOfBirth = DateTime.TryParse(f["dateOfBirth"], out var dob) ? dob : null,
            Address = f["address"].ToString(),
            Education = f["education"].ToString(),
            Experience = f["experience"].ToString(),
            Skills = f["skills"].ToString(),
            GithubUrl = f["githubUrl"].ToString(),
            LinkedInUrl = f["linkedInUrl"].ToString(),
            PortfolioUrl = f["portfolioUrl"].ToString(),
            TechSkillTags = f["techSkillTags"].ToString()
        });
        return Results.Redirect("/profile?saved=1");
    }
    catch (Exception ex) { return Results.Redirect("/profile?error=" + Enc(ex.Message)); }
}).RequireAuthorization(p => p.RequireRole(Roles.Student)).DisableAntiforgery();

app.MapPost("/profile/cv", async (HttpContext ctx, IProfileService svc) =>
{
    var uid = CurrentUserId(ctx);
    if (uid == 0) return Results.Redirect("/login");
    var f = await ctx.Request.ReadFormAsync();
    var file = f.Files["cv"];
    if (file is null || file.Length == 0)
        return Results.Redirect("/profile?cverror=" + Enc("Chưa chọn tệp CV."));
    using var ms = new MemoryStream();
    await file.CopyToAsync(ms);
    var (ok, err) = svc.SaveCv(uid, ms.ToArray(), file.FileName, file.ContentType);
    return ok ? Results.Redirect("/profile?cvsaved=1")
              : Results.Redirect("/profile?cverror=" + Enc(err ?? "Không lưu được CV."));
}).RequireAuthorization(p => p.RequireRole(Roles.Student)).DisableAntiforgery();

app.MapGet("/profile/cv/download", (HttpContext ctx, IProfileService svc) =>
{
    var uid = CurrentUserId(ctx);
    var p = uid == 0 ? null : svc.GetByUserId(uid);
    if (p is null || !p.HasCv) return Results.NotFound();
    return Results.File(p.CvData!, p.CvContentType ?? "application/octet-stream", p.CvFileName ?? "CV");
}).RequireAuthorization();

// ============================ APPLY (ATS-10) ============================
app.MapPost("/jobs/{id:int}/apply", (int id, HttpContext ctx, IApplicationService svc) =>
{
    var uid = CurrentUserId(ctx);
    if (uid == 0) return Results.Redirect("/login");
    svc.Apply(id, uid, out var message);
    return Results.Redirect("/positions?msg=" + Enc(message));
}).RequireAuthorization(p => p.RequireRole(Roles.Student)).DisableAntiforgery();

// ============================ MENTOR: CV + AI + STATUS (ATS-12→17) ============================
app.MapGet("/applications/{id:int}/cv", (int id, HttpContext ctx, IApplicationService svc) =>
{
    var a = svc.GetById(id);
    if (a is null) return Results.NotFound();
    if (!(IsAdmin(ctx) || a.Job?.CreatedById == CurrentUserId(ctx))) return Results.Forbid();  // #5
    // Ưu tiên CV đã đóng băng khi nộp; fallback CV hồ sơ cho dữ liệu cũ
    if (a.HasCvSnapshot)
        return Results.File(a.CvDataSnapshot!, a.CvContentTypeSnapshot ?? "application/octet-stream", a.CvFileNameSnapshot);
    var p = a.CandidateProfile;
    if (p is null || !p.HasCv) return Results.NotFound();
    return Results.File(p.CvData!, p.CvContentType ?? "application/octet-stream", p.CvFileName ?? "CV");
}).RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Mentor));

// ATS-13/14: AI đánh giá độ phù hợp + gợi ý lộ trình
app.MapPost("/applications/{id:int}/ai-evaluate", async (int id, HttpContext ctx, IApplicationService svc, IAiService ai) =>
{
    var a = svc.GetById(id);
    if (a?.CandidateProfile is null || a.Job is null)
        return Results.Redirect($"/applications/{id}?aierror=" + Enc("Không tìm thấy dữ liệu đơn."));
    if (!(IsAdmin(ctx) || a.Job.CreatedById == CurrentUserId(ctx))) return Results.Redirect("/denied");  // #5

    var p = a.CandidateProfile;
    // Đánh giá trên CV đã nộp (bản chụp), không phải CV hiện tại của hồ sơ
    var cvText = CvTextExtractor.Extract(a.CvDataSnapshot ?? p.CvData,
                                         a.HasCvSnapshot ? a.CvFileNameSnapshot : p.CvFileName);
    var candidateText = string.Join("\n", new[]
    {
        "Kỹ năng: " + p.TechSkillTags,
        "Skills: " + p.Skills,
        "Kinh nghiệm: " + p.Experience,
        "Học vấn: " + p.Education,
        "Nội dung CV: " + cvText
    });
    var jobText = string.Join("\n", new[]
    {
        "Vị trí: " + a.Job.Title + " (" + a.Job.Level + ")",
        "Danh mục: " + a.Job.Category,
        "Tech Stack yêu cầu: " + a.Job.TechStack,
        "Yêu cầu: " + a.Job.Requirements,
        "Mô tả: " + a.Job.Description
    });

    try
    {
        var eval = await ai.EvaluateAsync(candidateText, jobText);
        svc.SaveAiEvaluation(id, eval);
        return Results.Redirect($"/applications/{id}?aiscored=1");
    }
    catch (Exception ex)
    {
        return Results.Redirect($"/applications/{id}?aierror=" + Enc(ex.Message));
    }
}).RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Mentor)).DisableAntiforgery();

// ATS-16: Mentor điều chỉnh điểm (lý do bắt buộc)
app.MapPost("/applications/{id:int}/hr-score", async (int id, HttpContext ctx, IApplicationService svc) =>
{
    var f = await ctx.Request.ReadFormAsync();
    var note = f["hrNote"].ToString().Trim();
    var agree = f["agree"].ToString() == "1";

    var a = svc.GetById(id);
    if (a is null) return Results.Redirect("/jobs");
    if (!(IsAdmin(ctx) || a.Job?.CreatedById == CurrentUserId(ctx))) return Results.Redirect("/denied");  // #5

    if (agree)
    {
        var s = a.AiScore ?? 0;
        svc.SaveHrScore(id, s, "Đồng ý với đánh giá AI");
        return Results.Redirect($"/applications/{id}?hrsaved=1");
    }

    if (note.Length < 10)
        return Results.Redirect($"/applications/{id}?hrerror=" + Enc("Vui lòng nhập lý do điều chỉnh (≥ 10 ký tự)."));
    if (!int.TryParse(f["hrScore"], out var hr) || hr is < 0 or > 100)
        return Results.Redirect($"/applications/{id}?hrerror=" + Enc("Điểm điều chỉnh phải từ 0 đến 100."));

    svc.SaveHrScore(id, hr, note);
    return Results.Redirect($"/applications/{id}?hrsaved=1");
}).RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Mentor)).DisableAntiforgery();

// ATS-17: đổi trạng thái đơn
app.MapPost("/applications/{id:int}/status", async (int id, HttpContext ctx, IApplicationService svc) =>
{
    if (!svc.CanAccess(id, CurrentUserId(ctx), IsAdmin(ctx))) return Results.Redirect("/denied");  // #5
    var f = await ctx.Request.ReadFormAsync();
    svc.UpdateStatus(id, f["status"].ToString(), CurrentUserId(ctx), out var message);
    return Results.Redirect($"/applications/{id}?statusmsg=" + Enc(message));
}).RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Mentor)).DisableAntiforgery();

// ============================ NOTIFICATIONS (NTF-01) ============================
app.MapPost("/notifications/{id:int}/read", async (int id, HttpContext ctx, INotificationService svc) =>
{
    var f = await ctx.Request.ReadFormAsync();
    svc.MarkRead(id);
    var link = f["link"].ToString();
    return Results.Redirect(string.IsNullOrEmpty(link) ? "/my-applications" : link);
}).RequireAuthorization().DisableAntiforgery();

app.MapRazorComponents<App>();

app.Run();

// Cho phép WebApplicationFactory trong test (nếu dùng integration test sau này)
public partial class Program { }
