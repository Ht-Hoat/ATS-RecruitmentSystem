using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using ITCareerPlatform.Components;
using ITCareerPlatform.Data;
using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Claim lưu SecurityStamp của user trong cookie, để đối chiếu lại với CSDL mỗi request.
const string StampClaim = "itcp:stamp";
const string LoginRateLimitPolicy = "login";

// ---------- Blazor (server-rendered) + trạng thái đăng nhập ----------
builder.Services.AddRazorComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuthenticationStateProvider, HttpContextAuthStateProvider>();

// ---------- CSDL: SQL Server qua EF Core ----------
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException(
        "Thiếu ConnectionStrings:DefaultConnection. Ứng dụng dừng thay vì chạy với cấu hình mặc định.");

// appsettings.json chứa chuỗi kết nối localhost để tiện chạy máy cá nhân. Nếu bản triển khai
// thật quên ghi đè, tốt hơn là dừng hẳn còn hơn lặng lẽ khởi động và trỏ nhầm máy chủ.
if (!builder.Environment.IsDevelopment() &&
    (connectionString.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
     connectionString.Contains("(local)", StringComparison.OrdinalIgnoreCase)))
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection vẫn đang trỏ localhost ở môi trường không phải Development. " +
        "Hãy đặt chuỗi kết nối thật qua biến môi trường ConnectionStrings__DefaultConnection.");

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString));

// ---------- Xác thực Cookie + phân quyền theo Role ----------
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.AccessDeniedPath = "/denied";
        o.Cookie.Name = "ITCP.Auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;

        // Cookie mang sẵn Id + vai trò từ lúc đăng nhập. Nếu không đối chiếu lại,
        // Admin khóa tài khoản hay hạ vai trò cũng không có tác dụng cho tới khi
        // cookie hết hạn — phiên đang mở vẫn giữ nguyên mọi quyền cũ.
        o.Events.OnValidatePrincipal = async ctx =>
        {
            var uid = CurrentUser.Id(ctx.Principal);
            var stamp = ctx.Principal?.FindFirstValue(StampClaim);

            var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var current = uid <= 0
                ? null
                : await db.Users.AsNoTracking()
                                .Where(u => u.Id == uid)
                                .Select(u => new { u.IsActive, u.SecurityStamp })
                                .FirstOrDefaultAsync();

            var stillValid = current is not null
                             && current.IsActive
                             && stamp == current.SecurityStamp.ToString(CultureInfo.InvariantCulture);

            if (!stillValid)
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });

// Chặn theo mặc định: một trang hoặc endpoint mới quên khai báo quyền sẽ bị từ chối,
// thay vì lặng lẽ phục vụ cho mọi khách vãng lai.
builder.Services.AddAuthorization(o =>
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// ---------- Giới hạn tốc độ đăng nhập ----------
// Không có bước này thì việc dò mật khẩu chỉ tốn đúng một lần băm BCrypt mỗi lần thử.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(LoginRateLimitPolicy, ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// ---------- HttpClient cho Gemini (ATS-13) ----------
builder.Services.AddHttpClient("gemini", c => c.Timeout = TimeSpan.FromSeconds(30));

// ---------- Tầng nghiệp vụ ----------
builder.Services.AddScoped<IAuditService, AuditService>();
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

    // Dữ liệu mẫu chứa 6 tài khoản dùng chung mật khẩu "123456", trong đó có một Admin.
    // Chỉ nạp ở môi trường phát triển; lần khởi động đầu trên CSDL thật trước đây sẽ
    // tự tạo sẵn tài khoản quản trị đó.
    if (app.Environment.IsDevelopment())
        SeedData.Initialize(db);
}

// #11: ngoài môi trường Development, lỗi chưa bắt được sẽ hiển thị trang /error thân thiện
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/error");

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

// ---------- Helper dùng chung cho các endpoint ----------
static int CurrentUserId(HttpContext ctx) => CurrentUser.Id(ctx.User);
static bool IsAdmin(HttpContext ctx) => CurrentUser.IsAdmin(ctx.User);
static string Enc(string s) => Uri.EscapeDataString(s);

/// Chuyển trang chỉ tới đường dẫn nội bộ. Địa chỉ do người gửi cung cấp không bao giờ
/// được dùng trực tiếp — nếu không, endpoint trở thành bàn đạp chuyển hướng ra ngoài.
static IResult SafeRedirect(string? path, string fallback) =>
    !string.IsNullOrEmpty(path) && path.StartsWith('/') && !path.StartsWith("//")
        ? Results.LocalRedirect(path)
        : Results.LocalRedirect(fallback);

// ============================ AUTH (ATS-03, EXT-01) ============================
app.MapPost("/account/login", async (HttpContext ctx, IAuthService auth) =>
{
    var f = await ctx.Request.ReadFormAsync();
    var user = auth.Validate(f["email"].ToString(), f["password"].ToString());
    if (user is null) return Results.Redirect("/login?error=1");

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new(ClaimTypes.Name, user.FullName),
        new(ClaimTypes.Email, user.Email),
        new(ClaimTypes.Role, user.Role?.RoleName ?? Roles.Student),
        new(StampClaim, user.SecurityStamp.ToString(CultureInfo.InvariantCulture)),
    };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.LocalRedirect("/");
}).AllowAnonymous().DisableAntiforgery().RequireRateLimiting(LoginRateLimitPolicy);

app.MapPost("/account/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.LocalRedirect("/login");
}).DisableAntiforgery();

app.MapPost("/account/register", async (HttpContext ctx, IUserService svc) =>
{
    var f = await ctx.Request.ReadFormAsync();
    if (f["password"].ToString() != f["confirmPassword"].ToString())
        return Results.Redirect("/register?error=" + Enc("Mật khẩu xác nhận không khớp."));
    if (!svc.Register(f["fullName"].ToString(), f["email"].ToString(), f["password"].ToString(), out var error))
        return Results.Redirect("/register?error=" + Enc(error));
    return Results.LocalRedirect("/login?registered=1");
}).AllowAnonymous().DisableAntiforgery().RequireRateLimiting(LoginRateLimitPolicy);

// N1.A: người dùng tự đổi mật khẩu — mọi vai trò, không riêng Admin.
app.MapPost("/account/change-password", async (HttpContext ctx, IUserService svc) =>
{
    var uid = CurrentUserId(ctx);
    if (uid == 0) return Results.LocalRedirect("/login");

    var f = await ctx.Request.ReadFormAsync();
    var newPassword = f["newPassword"].ToString();
    if (newPassword != f["confirmPassword"].ToString())
        return Results.Redirect("/change-password?error=" + Enc("Mật khẩu xác nhận không khớp."));

    if (!svc.ChangePassword(uid, f["currentPassword"].ToString(), newPassword, out var error))
        return Results.Redirect("/change-password?error=" + Enc(error));

    // Đổi mật khẩu đã làm SecurityStamp tăng, nên cookie hiện tại hết hiệu lực NGAY.
    // Tự đăng xuất để người dùng nhận một trang đăng nhập có thông báo rõ ràng, thay vì
    // bị OnValidatePrincipal từ chối ở request kế tiếp mà không rõ chuyện gì xảy ra.
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.LocalRedirect("/login?pwchanged=1");
    // Cùng chính sách giới hạn tốc độ với đăng nhập: endpoint này cũng nhận mật khẩu hiện
    // tại, nên nếu không chặn thì nó thành một cửa dò mật khẩu thứ hai.
}).RequireAuthorization().DisableAntiforgery().RequireRateLimiting(LoginRateLimitPolicy);

// ============================ USERS (ATS-01, ATS-02) ============================
app.MapPost("/users/create", async (HttpContext ctx, IUserService svc) =>
{
    var f = await ctx.Request.ReadFormAsync();
    // Bản cũ: int.Parse trên trường form thô + không kiểm tra email trùng + không try/catch,
    // nên ba tình huống rất đời thường đều thành lỗi 500 chưa bắt.
    if (!int.TryParse(f["roleId"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var roleId))
        return Results.Redirect("/users/new?error=" + Enc("Vui lòng chọn vai trò hợp lệ."));

    try
    {
        svc.Create(f["fullName"].ToString(), f["email"].ToString(),
                   f["password"].ToString(), roleId, CurrentUserId(ctx));
        return Results.LocalRedirect("/users");
    }
    catch (ArgumentException ex)
    {
        return Results.Redirect("/users/new?error=" + Enc(ex.Message));
    }
}).RequireAuthorization(p => p.RequireRole(Roles.Admin)).DisableAntiforgery();

app.MapPost("/users/{id:int}/toggle-lock", (int id, HttpContext ctx, IUserService svc) =>
{
    if (id == CurrentUserId(ctx)) return Results.Redirect("/users?err=" + Enc("Không thể tự khóa tài khoản của mình."));
    svc.ToggleLock(id, CurrentUserId(ctx));
    return Results.LocalRedirect("/users");
}).RequireAuthorization(p => p.RequireRole(Roles.Admin)).DisableAntiforgery();

app.MapPost("/users/{id:int}/change-role", async (int id, HttpContext ctx, IUserService svc) =>
{
    if (id == CurrentUserId(ctx)) return Results.Redirect("/users?err=" + Enc("Không thể tự đổi vai trò của mình."));
    var f = await ctx.Request.ReadFormAsync();
    if (!int.TryParse(f["roleId"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var roleId))
        return Results.Redirect("/users?err=" + Enc("Vai trò không hợp lệ."));

    try
    {
        svc.ChangeRole(id, roleId, CurrentUserId(ctx));
        return Results.LocalRedirect("/users");
    }
    catch (ArgumentException ex)
    {
        return Results.Redirect("/users?err=" + Enc(ex.Message));
    }
}).RequireAuthorization(p => p.RequireRole(Roles.Admin)).DisableAntiforgery();

// ============================ JOBS (ATS-04, 05, 06) ============================
// Trình duyệt gửi <input type="number"> theo chuẩn HTML (dấu chấm thập phân), nên phải
// đọc bằng InvariantCulture. Với culture vi-VN, "15.5" từng được hiểu là 155.
static Job ReadJobForm(IFormCollection f, int actor) => new()
{
    Title = f["title"].ToString(),
    Description = f["description"].ToString(),
    Requirements = f["requirements"].ToString(),
    Location = f["location"].ToString(),
    SalaryMin = decimal.TryParse(f["salaryMin"], NumberStyles.Number, CultureInfo.InvariantCulture, out var mn) ? mn : 0,
    SalaryMax = decimal.TryParse(f["salaryMax"], NumberStyles.Number, CultureInfo.InvariantCulture, out var mx) ? mx : 0,
    Deadline = DateTime.TryParse(f["deadline"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
        ? d : DateTime.Today.AddMonths(1),
    Category = string.IsNullOrEmpty(f["category"]) ? "Khác" : f["category"].ToString(),
    TechStack = f["techStack"].ToString(),
    Level = string.IsNullOrEmpty(f["level"]) ? "Junior" : f["level"].ToString(),
    CreatedById = actor
};

app.MapPost("/jobs/create", async (HttpContext ctx, IJobService svc) =>
{
    var f = await ctx.Request.ReadFormAsync();
    try { svc.Create(ReadJobForm(f, CurrentUserId(ctx))); return Results.LocalRedirect("/jobs"); }
    catch (Exception ex) { return Results.Redirect("/jobs/new?error=" + Enc(ex.Message)); }
}).RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Mentor)).DisableAntiforgery();

app.MapPost("/jobs/{id:int}/update", async (int id, HttpContext ctx, IJobService svc) =>
{
    var f = await ctx.Request.ReadFormAsync();
    try { svc.Update(id, ReadJobForm(f, CurrentUserId(ctx)), CurrentUserId(ctx)); return Results.LocalRedirect("/jobs"); }
    catch (UnauthorizedAccessException) { return Results.LocalRedirect("/denied"); }
    catch (Exception ex) { return Results.Redirect($"/jobs/edit/{id}?error=" + Enc(ex.Message)); }
}).RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Mentor)).DisableAntiforgery();

// ATS-06: đóng/mở lại tin — nay truyền người thực hiện xuống service để kiểm tra quyền
// sở hữu. Trước đây hai endpoint này chỉ chặn theo vai trò, nên bất kỳ Mentor nào cũng
// đóng được tin tuyển dụng của Mentor khác chỉ bằng cách bấm nút hiện sẵn trên /jobs.
app.MapPost("/jobs/{id:int}/close", (int id, HttpContext ctx, IJobService svc) =>
{
    try { svc.Close(id, CurrentUserId(ctx)); return Results.LocalRedirect("/jobs"); }
    catch (UnauthorizedAccessException) { return Results.LocalRedirect("/denied"); }
    catch (InvalidOperationException ex) { return Results.Redirect("/jobs?err=" + Enc(ex.Message)); }
}).RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Mentor)).DisableAntiforgery();

app.MapPost("/jobs/{id:int}/reopen", (int id, HttpContext ctx, IJobService svc) =>
{
    try { svc.Reopen(id, CurrentUserId(ctx)); return Results.LocalRedirect("/jobs"); }
    catch (UnauthorizedAccessException) { return Results.LocalRedirect("/denied"); }
    catch (InvalidOperationException ex) { return Results.Redirect("/jobs?err=" + Enc(ex.Message)); }
}).RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Mentor)).DisableAntiforgery();

// ============================ PROFILE (ATS-08, ATS-09) ============================
app.MapPost("/profile/save", async (HttpContext ctx, IProfileService svc) =>
{
    var uid = CurrentUserId(ctx);
    if (uid == 0) return Results.LocalRedirect("/login");
    var f = await ctx.Request.ReadFormAsync();
    try
    {
        svc.Save(uid, new CandidateProfile
        {
            FullName = f["fullName"].ToString(),
            Email = f["email"].ToString(),
            Phone = f["phone"].ToString(),
            DateOfBirth = DateTime.TryParse(f["dateOfBirth"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var dob) ? dob : null,
            Address = f["address"].ToString(),
            Education = f["education"].ToString(),
            Experience = f["experience"].ToString(),
            Skills = f["skills"].ToString(),
            GithubUrl = f["githubUrl"].ToString(),
            LinkedInUrl = f["linkedInUrl"].ToString(),
            PortfolioUrl = f["portfolioUrl"].ToString(),
            TechSkillTags = f["techSkillTags"].ToString()
        });
        return Results.LocalRedirect("/profile?saved=1");
    }
    catch (ArgumentException ex) { return Results.Redirect("/profile?error=" + Enc(ex.Message)); }
}).RequireAuthorization(p => p.RequireRole(Roles.Student)).DisableAntiforgery();

app.MapPost("/profile/cv", async (HttpContext ctx, IProfileService svc) =>
{
    var uid = CurrentUserId(ctx);
    if (uid == 0) return Results.LocalRedirect("/login");
    var f = await ctx.Request.ReadFormAsync();
    var file = f.Files["cv"];
    if (file is null || file.Length == 0)
        return Results.Redirect("/profile?cverror=" + Enc("Chưa chọn tệp CV."));

    // Từ chối theo kích thước KHAI BÁO, trước khi đọc byte nào. Bản cũ nạp cả tệp vào
    // MemoryStream rồi ToArray() (hai bản sao trên Large Object Heap) mới đi kiểm tra 5MB.
    if (file.Length > CvScanner.MaxBytes)
        return Results.Redirect("/profile?cverror=" + Enc("Kích thước tệp vượt quá 5MB cho phép."));

    using var ms = new MemoryStream(capacity: (int)file.Length);
    await file.CopyToAsync(ms);
    var (ok, err) = svc.SaveCv(uid, ms.ToArray(), file.FileName, file.ContentType);
    return ok ? Results.LocalRedirect("/profile?cvsaved=1")
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
    if (uid == 0) return Results.LocalRedirect("/login");
    svc.Apply(id, uid, out var message);
    return Results.Redirect("/positions?msg=" + Enc(message));
}).RequireAuthorization(p => p.RequireRole(Roles.Student)).DisableAntiforgery();

// ============================ MENTOR: CV + AI + STATUS (ATS-12→17) ============================
app.MapGet("/applications/{id:int}/cv", (int id, HttpContext ctx, IApplicationService svc) =>
{
    // Quyền sở hữu hỏi qua service — một chỗ duy nhất phát biểu luật, thay vì lặp lại vị ngữ.
    if (!svc.CanAccess(id, CurrentUserId(ctx), IsAdmin(ctx))) return Results.Forbid();

    var a = svc.GetById(id);
    if (a is null) return Results.NotFound();

    // Ưu tiên CV đã đóng băng khi nộp; fallback CV hồ sơ cho dữ liệu cũ
    if (a.HasCvSnapshot)
        return Results.File(a.CvDataSnapshot!, a.CvContentTypeSnapshot ?? "application/octet-stream", a.CvFileNameSnapshot);
    var p = a.CandidateProfile;
    if (p is null || !p.HasCv) return Results.NotFound();
    return Results.File(p.CvData!, p.CvContentType ?? "application/octet-stream", p.CvFileName ?? "CV");
}).RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Mentor));

// Dựng dữ liệu đưa vào AI từ một đơn. Dùng chung cho chấm điểm (ATS-13/14) và sinh câu
// hỏi (N1.C): hai đường phải đọc CÙNG một nguồn, nếu không thì điểm chấm trên CV bản chụp
// còn câu hỏi lại soạn từ CV hiện tại của hồ sơ — hai kết quả nói về hai ứng viên khác nhau.
static AiEvaluationInput BuildAiInput(Application a, CandidateProfile p)
{
    // Luôn là CV ĐÃ NỘP (bản chụp), không phải CV hiện tại của hồ sơ.
    var cvText = CvTextExtractor.Extract(a.CvDataSnapshot ?? p.CvData,
                                         a.HasCvSnapshot ? a.CvFileNameSnapshot : p.CvFileName);

    // Danh sách công nghệ đi vào ô riêng có cấu trúc; phần văn bản tự do chỉ để mô hình đọc.
    return new AiEvaluationInput(
        CandidateText: string.Join("\n", new[]
        {
            "Kỹ năng: " + p.Skills,
            "Kinh nghiệm: " + p.Experience,
            "Học vấn: " + p.Education,
            "Nội dung CV: " + cvText
        }),
        JobText: string.Join("\n", new[]
        {
            "Vị trí: " + a.Job!.Title + " (" + a.Job.Level + ")",
            "Danh mục: " + a.Job.Category,
            "Yêu cầu: " + a.Job.Requirements,
            "Mô tả: " + a.Job.Description
        }),
        CandidateTech: p.TechSkillTags,
        RequiredTech: a.Job.TechStack);
}

// ATS-13/14: AI đánh giá độ phù hợp + gợi ý lộ trình
app.MapPost("/applications/{id:int}/ai-evaluate", async (int id, HttpContext ctx, IApplicationService svc, IAiService ai) =>
{
    if (!svc.CanAccess(id, CurrentUserId(ctx), IsAdmin(ctx))) return Results.LocalRedirect("/denied");

    var a = svc.GetById(id);
    if (a?.CandidateProfile is null || a.Job is null)
        return Results.Redirect($"/applications/{id}?aierror=" + Enc("Không tìm thấy dữ liệu đơn."));

    try
    {
        var eval = await ai.EvaluateAsync(BuildAiInput(a, a.CandidateProfile), ctx.RequestAborted);
        svc.SaveAiEvaluation(id, eval);
        return Results.Redirect($"/applications/{id}?aiscored=1");
    }
    catch (Exception ex)
    {
        return Results.Redirect($"/applications/{id}?aierror=" + Enc(ex.Message));
    }
}).RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Mentor)).DisableAntiforgery();

// N1.C: sinh bộ câu hỏi phỏng vấn từ CV đã nộp + JD. Kết quả được LƯU, vì trang render
// tĩnh: sinh xong rồi redirect thì không còn gì để hiển thị, và mỗi lần mở lại trang sẽ
// tốn thêm một lượt gọi Gemini.
app.MapPost("/applications/{id:int}/ai-questions", async (int id, HttpContext ctx, IApplicationService svc, IAiService ai) =>
{
    if (!svc.CanAccess(id, CurrentUserId(ctx), IsAdmin(ctx))) return Results.LocalRedirect("/denied");

    var a = svc.GetById(id);
    if (a?.CandidateProfile is null || a.Job is null)
        return Results.Redirect($"/applications/{id}?qerror=" + Enc("Không tìm thấy dữ liệu đơn."));

    try
    {
        var set = await ai.GenerateQuestionsAsync(BuildAiInput(a, a.CandidateProfile), ctx.RequestAborted);
        svc.SaveAiQuestions(id, set);
        return Results.Redirect($"/applications/{id}?qgenerated=1");
    }
    catch (Exception ex)
    {
        return Results.Redirect($"/applications/{id}?qerror=" + Enc(ex.Message));
    }
}).RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Mentor)).DisableAntiforgery();

// ATS-16: Mentor điều chỉnh điểm (lý do bắt buộc)
app.MapPost("/applications/{id:int}/hr-score", async (int id, HttpContext ctx, IApplicationService svc) =>
{
    if (!svc.CanAccess(id, CurrentUserId(ctx), IsAdmin(ctx))) return Results.LocalRedirect("/denied");

    var f = await ctx.Request.ReadFormAsync();
    var note = f["hrNote"].ToString().Trim();
    var agree = f["agree"].ToString() == "1";

    var a = svc.GetDetail(id);
    if (a is null) return Results.LocalRedirect("/jobs");

    if (agree)
    {
        if (a.AiScore is null)
            return Results.Redirect($"/applications/{id}?hrerror=" + Enc("Chưa có đánh giá AI để đồng ý."));
        svc.SaveHrScore(id, a.AiScore.Value, "Đồng ý với đánh giá AI", CurrentUserId(ctx));
        return Results.Redirect($"/applications/{id}?hrsaved=1");
    }

    if (note.Length < 10)
        return Results.Redirect($"/applications/{id}?hrerror=" + Enc("Vui lòng nhập lý do điều chỉnh (≥ 10 ký tự)."));
    if (!int.TryParse(f["hrScore"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hr) || hr is < 0 or > 100)
        return Results.Redirect($"/applications/{id}?hrerror=" + Enc("Điểm điều chỉnh phải từ 0 đến 100."));

    svc.SaveHrScore(id, hr, note, CurrentUserId(ctx));
    return Results.Redirect($"/applications/{id}?hrsaved=1");
}).RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Mentor)).DisableAntiforgery();

// ATS-17: đổi trạng thái đơn · N1.E: kèm lịch phỏng vấn khi chuyển sang "Phỏng vấn"
app.MapPost("/applications/{id:int}/status", async (int id, HttpContext ctx, IApplicationService svc) =>
{
    if (!svc.CanAccess(id, CurrentUserId(ctx), IsAdmin(ctx))) return Results.LocalRedirect("/denied");
    var f = await ctx.Request.ReadFormAsync();
    var status = f["status"].ToString();

    // Ba ô lịch chỉ được đọc khi Mentor thực sự chọn "Phỏng vấn". Form luôn gửi chúng lên
    // (trang render tĩnh, không ẩn được ở phía server), nên nếu đọc vô điều kiện thì một
    // lần chuyển sang "Từ chối" cũng ghi đè lịch hẹn đang có.
    InterviewSchedule? schedule = null;
    if (status == ApplicationStatus.Interview)
    {
        // <input type="datetime-local"> gửi "2026-09-20T14:30" theo chuẩn HTML, nên đọc
        // bằng InvariantCulture — giống mọi ô ngày/số khác trong dự án.
        DateTime.TryParse(f["interviewAt"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var at);
        schedule = new InterviewSchedule(at, f["interviewLink"].ToString(), f["interviewNote"].ToString());
    }

    var ok = svc.UpdateStatus(id, status, schedule, CurrentUserId(ctx), out var message);
    // Thành công và thất bại đi về hai tham số khác nhau: gộp chung thì một lời từ chối
    // ("thời gian phỏng vấn phải ở tương lai") hiện ra trong khung báo thành công màu xanh.
    return Results.Redirect($"/applications/{id}?" + (ok ? "statusmsg=" : "statuserr=") + Enc(message));
}).RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Mentor)).DisableAntiforgery();

// N1.B: ghi chú nội bộ về ứng viên — chỉ Mentor chủ tin và Admin, không bao giờ hiện cho SV.
app.MapPost("/applications/{id:int}/internal-note", async (int id, HttpContext ctx, IApplicationService svc) =>
{
    if (!svc.CanAccess(id, CurrentUserId(ctx), IsAdmin(ctx))) return Results.LocalRedirect("/denied");
    var f = await ctx.Request.ReadFormAsync();
    svc.SaveInternalNote(id, f["internalNote"].ToString(), CurrentUserId(ctx));
    return Results.Redirect($"/applications/{id}?notesaved=1");
}).RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Mentor)).DisableAntiforgery();

// ============================ NOTIFICATIONS (NTF-01) ============================
app.MapPost("/notifications/{id:int}/read", (int id, HttpContext ctx, INotificationService svc) =>
{
    // Chỉ đánh dấu thông báo của chính mình, và chuyển tới đường dẫn ĐÃ LƯU trong CSDL.
    // Bản cũ nhận mỗi id (ai cũng xóa được huy hiệu của người khác) và chuyển tới địa chỉ
    // lấy thẳng từ body — tức là một endpoint chuyển hướng ra ngoài miền.
    var link = svc.MarkRead(id, CurrentUserId(ctx));
    return SafeRedirect(link, "/my-applications");
}).RequireAuthorization().DisableAntiforgery();

app.MapRazorComponents<App>();

app.Run();

// Cho phép WebApplicationFactory trong test (nếu dùng integration test sau này)
public partial class Program { }
