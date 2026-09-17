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
// P0-3: OnValidatePrincipal đã truy vấn Users mỗi request để đối chiếu SecurityStamp; đọc
// luôn cờ "buộc đổi mật khẩu" trong cùng truy vấn đó và gửi sang middleware qua Items —
// rẻ hơn một claim trong cookie (claim có thể cũ tới 8 tiếng) và không tốn thêm lần đọc nào.
const string MustChangePasswordItem = "itcp:mustchangepw";

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
                                .Select(u => new { u.IsActive, u.SecurityStamp, u.MustChangePassword })
                                .FirstOrDefaultAsync();

            var stillValid = current is not null
                             && current.IsActive
                             && stamp == current.SecurityStamp.ToString(CultureInfo.InvariantCulture);

            if (!stillValid)
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            if (current!.MustChangePassword)
                ctx.HttpContext.Items[MustChangePasswordItem] = true;
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

// ---------- Nguồn thời gian (P0-2) ----------
// Một nguồn duy nhất, tiêm được, để test cố định thời điểm mà không cần package ngoài.
// Mọi service đọc giờ qua TimeProvider rồi quy về UTC; DateTime.Now không còn xuất hiện ở
// tầng nghiệp vụ, vì giá trị của nó phụ thuộc múi giờ của máy chủ chứ không phải của người dùng.
builder.Services.AddSingleton(TimeProvider.System);

// ---------- Tầng nghiệp vụ ----------
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ICompanyService, CompanyService>();   // P1-1
builder.Services.AddScoped<IJobService, JobService>();
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IApplicationService, ApplicationService>();
builder.Services.AddScoped<IAiService, GeminiAiService>();
// P1-2: dựng dữ liệu đưa vào AI ở MỘT chỗ, cho cả ba đường (chấm điểm, sinh câu hỏi, tự kiểm tra).
builder.Services.AddScoped<IAiInputBuilder, AiInputBuilder>();
builder.Services.AddScoped<ISelfCheckService, SelfCheckService>();

// ---------- Email (P1-3) ----------
// Chọn bản triển khai NGAY LÚC KHỞI ĐỘNG theo cấu hình, giống cách GeminiAiService xử lý
// khóa API. Đăng ký bản SMTP rồi để nó tự ném ngoại lệ khi thiếu cấu hình thì mỗi email
// thành một dòng lỗi trong log, và không có gì nói cho người vận hành biết vì sao.
var smtpConfigured = !string.IsNullOrWhiteSpace(builder.Configuration["Smtp:Host"])
                     && !string.IsNullOrWhiteSpace(builder.Configuration["Smtp:From"] ?? builder.Configuration["Smtp:User"]);
if (smtpConfigured)
    builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
else
    builder.Services.AddScoped<IEmailSender, NullEmailSender>();

// Tiến trình nền quét hàng đợi email 30 giây một lần.
builder.Services.AddHostedService<OutboxSender>();

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

    // ---------- Tài khoản quản trị đầu tiên ----------
    // Chạy ở MỌI môi trường nhưng không làm gì khi đã có Admin, nên ở Development đây chỉ
    // là một lần COUNT rồi thôi (SeedData đã tạo admin@itcp.vn ngay bên trên).
    var users = scope.ServiceProvider.GetRequiredService<IUserService>();
    if (users.CountByRole(Roles.AdminId) == 0)
    {
        var bootstrapEmail = app.Configuration["Bootstrap:AdminEmail"];
        var bootstrapPassword = app.Configuration["Bootstrap:AdminPassword"];

        if (string.IsNullOrWhiteSpace(bootstrapEmail) || string.IsNullOrWhiteSpace(bootstrapPassword))
        {
            // Cảnh báo chứ không dừng hẳn: ứng dụng vẫn phục vụ được Sinh viên và Mentor,
            // và một bản triển khai đang chạy tốt không đáng bị chặn khởi động vì lý do này.
            app.Logger.LogWarning(
                "Chưa có tài khoản quản trị nào, và cũng chưa cấu hình Bootstrap:AdminEmail / " +
                "Bootstrap:AdminPassword. Hệ thống vẫn chạy nhưng KHÔNG ai quản trị được: " +
                "đăng ký công khai chỉ tạo Sinh viên, còn tạo tài khoản lại đòi sẵn quyền Admin. " +
                "Hãy đặt hai biến môi trường Bootstrap__AdminEmail và Bootstrap__AdminPassword " +
                "rồi khởi động lại.");
        }
        else if (users.TryCreateFirstAdmin(
                     app.Configuration["Bootstrap:AdminFullName"] ?? "Quản trị hệ thống",
                     bootstrapEmail, bootstrapPassword, out var bootstrapError))
        {
            // Ghi email để biết tài khoản nào vừa được tạo, nhưng TUYỆT ĐỐI không ghi mật
            // khẩu: log thường được gom về một nơi mà nhiều người đọc được.
            app.Logger.LogInformation(
                "Đã tạo tài khoản quản trị đầu tiên cho {Email}. Hãy đổi mật khẩu sau lần " +
                "đăng nhập đầu và gỡ hai biến môi trường Bootstrap__* khỏi cấu hình.",
                bootstrapEmail);
        }
        else
        {
            app.Logger.LogError("Không tạo được tài khoản quản trị đầu tiên: {Error}", bootstrapError);
        }
    }
}

// #11: ngoài môi trường Development, lỗi chưa bắt được sẽ hiển thị trang /error thân thiện
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/error");

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

// ---------- P0-3: chốt buộc đổi mật khẩu ----------
// Đặt ở ĐÚNG MỘT chỗ thay vì rải điều kiện vào từng trang: trang nào quên là trang đó lọt,
// và người vừa bị reset mật khẩu vẫn dùng được hệ thống bình thường bằng mật khẩu tạm mà
// Admin đọc qua điện thoại — tức là mật khẩu tạm trở thành mật khẩu thật.
app.Use(async (ctx, next) =>
{
    if (ctx.Items.TryGetValue(MustChangePasswordItem, out var flag) && flag is true)
    {
        var path = ctx.Request.Path;
        // Đúng ba đường được đi: trang đổi mật khẩu, endpoint xử lý nó, và đăng xuất.
        // Thiếu đường đăng xuất thì người dùng bị kẹt hẳn nếu không nhớ mật khẩu tạm.
        var allowed = path.StartsWithSegments("/change-password")
                      || path.StartsWithSegments("/account/change-password")
                      || path.StartsWithSegments("/account/logout");
        if (!allowed)
        {
            ctx.Response.Redirect("/change-password?forced=1");
            return;
        }
    }
    await next();
});

// ---------- Helper dùng chung cho các endpoint ----------
static int CurrentUserId(HttpContext ctx) => CurrentUser.Id(ctx.User);
static bool IsAdmin(HttpContext ctx) => CurrentUser.IsAdmin(ctx.User);
static string Enc(string s) => Uri.EscapeDataString(s);

/// Ghi ngoại lệ ngoài dự kiến vào log rồi trả về một câu chung cho người dùng.
///
/// ArgumentException và InvalidOperationException do tầng nghiệp vụ ném ra là thông điệp
/// CỐ Ý viết cho người dùng đọc ("Lương tối đa phải ≥ lương tối thiểu") — những chỗ đó vẫn
/// hiện nguyên văn. Còn mọi ngoại lệ khác là chuyện nội bộ: một SqlException đi thẳng vào
/// thanh địa chỉ sẽ tiết lộ tên bảng, tên cột và cấu trúc CSDL cho bất kỳ ai đứng cạnh màn
/// hình, mà vẫn không nói được cho người dùng điều gì hữu ích.
static string SafeError(HttpContext ctx, Exception ex, string what)
{
    ctx.RequestServices.GetRequiredService<ILoggerFactory>()
       .CreateLogger("ITCareerPlatform.Endpoints")
       .LogError(ex, "Lỗi ngoài dự kiến khi {What}", what);

    return "Có lỗi hệ thống, vui lòng thử lại. Quản trị viên có thể xem chi tiết trong log máy chủ.";
}

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
    var emailVal = f["email"].ToString();
    var user = auth.Validate(emailVal, f["password"].ToString());
    if (user is null) return Results.Redirect("/login?error=1&email=" + Enc(emailVal));

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

// P0-3 + P1-3: Admin đặt lại mật khẩu.
//
// Có SMTP thì mật khẩu tạm đi thẳng vào hộp thư người dùng và KHÔNG bao giờ xuất hiện trên
// màn hình hay trong thanh địa chỉ. Chưa cấu hình SMTP (hoặc lần gửi vừa rồi hỏng) thì mới
// lùi về cách cũ — hiện một lần cho Admin đọc lại cho người dùng.
//
// Email này gửi TRỰC TIẾP chứ không qua hàng đợi EmailOutbox như ba email trạng thái: xếp
// hàng nghĩa là mật khẩu nằm ở dạng rõ trong một cột CSDL cho tới khi gửi xong, trong khi
// Admin lại đang đứng chờ ngay đó để biết kết quả. Gửi thẳng vừa không lưu lại gì, vừa trả
// lời được ngay là đã tới hay chưa.
app.MapPost("/users/{id:int}/reset-password", async (int id, HttpContext ctx, IUserService svc, IEmailSender email) =>
{
    if (!svc.ResetPassword(id, CurrentUserId(ctx), out var tempPassword, out var error))
        return Results.Redirect("/users?err=" + Enc(error));

    var target = svc.GetAll().FirstOrDefault(u => u.Id == id);
    if (email.IsConfigured && target is not null)
    {
        try
        {
            await email.SendAsync(new EmailMessage(
                target.Email,
                "Mật khẩu tạm thời — IT Career Platform",
                string.Join(Environment.NewLine, new[]
                {
                    $"Xin chào {target.FullName},",
                    "",
                    "Quản trị viên vừa đặt lại mật khẩu cho tài khoản của bạn.",
                    $"Mật khẩu tạm thời: {tempPassword}",
                    "",
                    "Hãy đăng nhập và đổi mật khẩu ngay — hệ thống sẽ giữ bạn ở trang Đổi mật khẩu",
                    "cho tới khi bạn đổi xong.",
                    "",
                    "Trân trọng,",
                    "IT Career Platform"
                })), ctx.RequestAborted);

            return Results.Redirect("/users?msg=" + Enc(
                $"Đã gửi mật khẩu tạm tới {target.Email}. Người dùng phải đổi mật khẩu ngay khi đăng nhập."));
        }
        catch (Exception ex)
        {
            // Gửi hỏng KHÔNG được làm hỏng việc đặt lại mật khẩu — mật khẩu đã đổi rồi. Lùi
            // về hiện trên màn hình, nếu không thì tài khoản đó không ai vào được nữa.
            SafeError(ctx, ex, $"gửi mật khẩu tạm cho tài khoản #{id}");
            return Results.Redirect("/users?tempPw=" + Enc(tempPassword) + "&mailfailed=1");
        }
    }

    return Results.Redirect("/users?tempPw=" + Enc(tempPassword));
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
    // Hạn nộp là một NGÀY: mặc định một tháng kể từ hôm nay THEO GIỜ VIỆT NAM, không phải
    // theo ngày của container (vốn chạy UTC và lệch một ngày trong khung 00:00-07:00 giờ VN).
    Deadline = DateTime.TryParse(f["deadline"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
        ? d.Date : VietnamDateHelper.Today().AddMonths(1),
    Category = string.IsNullOrEmpty(f["category"]) ? "Khác" : f["category"].ToString(),
    TechStack = f["techStack"].ToString(),
    Level = string.IsNullOrEmpty(f["level"]) ? "Junior" : f["level"].ToString(),
    // N2.C: chỉ nhận giá trị thuộc danh sách hợp lệ; luật đầy đủ vẫn được JobService kiểm lại.
    EmploymentType = Job.IsValidEmploymentType(f["employmentType"]) ? f["employmentType"].ToString() : "Onsite",
    CreatedById = actor
};

app.MapPost("/jobs/create", async (HttpContext ctx, IJobService svc) =>
{
    var f = await ctx.Request.ReadFormAsync();
    try { svc.Create(ReadJobForm(f, CurrentUserId(ctx))); return Results.LocalRedirect("/jobs"); }
    // Luật nghiệp vụ do JobService.Validate phát biểu — hiện nguyên văn cho người đăng tin.
    catch (ArgumentException ex) { return Results.Redirect("/jobs/new?error=" + Enc(ex.Message)); }
    catch (Exception ex) { return Results.Redirect("/jobs/new?error=" + Enc(SafeError(ctx, ex, "tạo tin tuyển dụng"))); }
}).RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Mentor)).DisableAntiforgery();

app.MapPost("/jobs/{id:int}/update", async (int id, HttpContext ctx, IJobService svc) =>
{
    var f = await ctx.Request.ReadFormAsync();
    try { svc.Update(id, ReadJobForm(f, CurrentUserId(ctx)), CurrentUserId(ctx)); return Results.LocalRedirect("/jobs"); }
    catch (UnauthorizedAccessException) { return Results.LocalRedirect("/denied"); }
    // ArgumentException: luật nghiệp vụ. InvalidOperationException: "tin đã đóng, mở lại
    // trước khi sửa". Cả hai đều là câu viết sẵn cho người dùng đọc.
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
    { return Results.Redirect($"/jobs/edit/{id}?error=" + Enc(ex.Message)); }
    catch (Exception ex) { return Results.Redirect($"/jobs/edit/{id}?error=" + Enc(SafeError(ctx, ex, $"sửa tin #{id}"))); }
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
            // Bỏ trống hoặc gõ chữ thì hiểu là 0 năm; khoảng hợp lệ do [Range] trên entity
            // kiểm lại ở ProfileService, không tin vào thuộc tính min/max của thẻ input.
            YearsOfExperience = int.TryParse(f["yearsOfExperience"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var yoe) ? yoe : 0,
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
app.MapPost("/jobs/{id:int}/apply", async (int id, HttpContext ctx, IApplicationService svc) =>
{
    var uid = CurrentUserId(ctx);
    if (uid == 0) return Results.LocalRedirect("/login");
    // Trước P0-1 endpoint này không đọc form. Một request tự tạo không kèm thân form sẽ làm
    // ReadFormAsync ném ngoại lệ, nên hỏi HasFormContentType trước: thiếu form nghĩa là
    // không có "from", tức là quay về danh sách — chứ không phải lỗi 500.
    var f = ctx.Request.HasFormContentType ? await ctx.Request.ReadFormAsync() : null;

    // Cùng quy ước với /applications/{id}/status: thành công và thất bại đi về hai tham số
    // khác nhau. Bản cũ vứt giá trị trả về đi, nên "Tin đã quá hạn nộp hồ sơ" hiện lên
    // trong khung báo thành công màu xanh.
    var ok = svc.Apply(id, uid, out var message);
    var query = (ok ? "msg=" : "err=") + Enc(message);

    // P0-1: ứng tuyển từ trang chi tiết thì phải quay lại CHÍNH trang đó, nếu không sinh
    // viên vừa đọc xong JD lại bị ném về danh sách và mất chỗ đang đứng.
    //
    // Form chỉ gửi được đúng một từ khóa "detail", KHÔNG gửi đường dẫn. Nhận đường dẫn thô
    // rồi redirect theo nó là mở sẵn một lỗ chuyển hướng ra ngoài miền: kẻ tấn công dựng
    // link /jobs/1/apply?from=//evil.example và nạn nhân tin rằng mình vẫn ở trên hệ thống.
    var from = f?["from"].ToString();
    return Results.Redirect(from == "detail"
        ? $"/positions/{id}?" + query
        : "/positions?" + query);
}).RequireAuthorization(p => p.RequireRole(Roles.Student)).DisableAntiforgery();

// P1-2: sinh viên tự chạy đánh giá độ phù hợp với một tin, trước khi ứng tuyển.
// Hạn mức, điều kiện hồ sơ/CV và điều kiện tin còn mở đều do service phát biểu — endpoint
// chỉ chuyển câu trả lời về đúng trang chi tiết mà sinh viên đang đứng.
app.MapPost("/positions/{id:int}/self-check", async (int id, HttpContext ctx, ISelfCheckService svc) =>
{
    var uid = CurrentUserId(ctx);
    if (uid == 0) return Results.LocalRedirect("/login");

    try
    {
        var (ok, message) = await svc.RunAsync(userId: uid, jobId: id, ct: ctx.RequestAborted);
        return Results.Redirect($"/positions/{id}?" + (ok ? "msg=" : "err=") + Enc(message));
    }
    catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
    {
        // Người dùng bỏ trang giữa chừng — không còn ai để hiện thông báo, và đây không
        // phải lỗi nên cũng không ghi log.
        return Results.Empty;
    }
    catch (Exception ex)
    {
        return Results.Redirect($"/positions/{id}?err=" + Enc(SafeError(ctx, ex, $"tự kiểm tra độ phù hợp với tin #{id}")));
    }
}).RequireAuthorization(p => p.RequireRole(Roles.Student)).DisableAntiforgery();

// P0-4: sinh viên rút đơn. Quyền sở hữu do service kiểm (CandidateProfile.UserId), không
// kiểm ở đây — nếu viết lại vị ngữ tại chỗ thì hai nơi sẽ trôi khỏi nhau theo thời gian.
app.MapPost("/applications/{id:int}/withdraw", (int id, HttpContext ctx, IApplicationService svc) =>
{
    var uid = CurrentUserId(ctx);
    if (uid == 0) return Results.LocalRedirect("/login");

    var ok = svc.Withdraw(id, uid, out var message);
    return Results.Redirect("/my-applications?" + (ok ? "msg=" : "err=") + Enc(message));
}).RequireAuthorization(p => p.RequireRole(Roles.Student)).DisableAntiforgery();

// ============================ P2-1: XUẤT CSV + THAO TÁC HÀNG LOẠT ============================
// Xuất đúng tập đang hiện trên màn hình: cùng quyền (CanModify) và cùng bộ lọc (dựng bằng
// ApplicantFilter.FromQuery, chung một hàm với trang). Tệp CSV khác với bảng đang xem là
// một sai lệch người dùng không có cách nào phát hiện.
app.MapGet("/jobs/{id:int}/applicants/export", (int id, HttpContext ctx, IJobService jobs, IApplicationService svc) =>
{
    if (!jobs.CanModify(id, CurrentUserId(ctx))) return Results.Forbid();

    var job = jobs.GetById(id);
    if (job is null) return Results.NotFound();

    var q = ctx.Request.Query;
    var filter = ApplicantFilter.FromQuery(
        q["status"], q["cv"], q["band"], q["level"], q["tech"].Where(x => x is not null)!, job.TechStackList);

    var items = svc.GetByJob(id, q["sort"].ToString() is { Length: > 0 } sort ? sort : "date", filter);

    // Tên tệp chỉ gồm mã tin và ngày — KHÔNG lấy tiêu đề tin do người dùng nhập, vì tiêu đề
    // có thể chứa dấu gạch chéo, dấu ngoặc kép hoặc xuống dòng và làm hỏng header
    // Content-Disposition.
    var fileName = $"ung-vien-tin-{id}-{VietnamDateHelper.Today():yyyyMMdd}.csv";
    return Results.File(CsvExport.Applicants(items), "text/csv; charset=utf-8", fileName);
}).RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Mentor));

app.MapPost("/jobs/{id:int}/applicants/bulk-status", async (int id, HttpContext ctx, IJobService jobs, IApplicationService svc) =>
{
    // Quyền kiểm theo TIN, một lần. UpdateStatus bên trong không nhận actor để kiểm quyền
    // sở hữu đơn, nên nếu bỏ bước này thì bất kỳ Mentor nào cũng đổi được đơn của tin khác
    // chỉ bằng cách gửi id đơn tùy ý.
    if (!jobs.CanModify(id, CurrentUserId(ctx))) return Results.LocalRedirect("/denied");

    var f = await ctx.Request.ReadFormAsync();
    var status = f["status"].ToString();
    var ids = f["applicationId"]
        .Where(v => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        .Select(v => int.Parse(v!, NumberStyles.Integer, CultureInfo.InvariantCulture))
        .ToList();

    if (ids.Count == 0)
        return Results.Redirect($"/jobs/{id}/applicants?err=" + Enc("Chưa chọn đơn nào."));

    // Chỉ xử lý những đơn THỰC SỰ thuộc tin này. Danh sách id đến từ form, và form là thứ
    // người gửi request sửa được — thiếu bước lọc này thì quyền kiểm ở trên thành vô nghĩa.
    var owned = svc.GetByJob(id, "date", null).Select(a => a.Id).ToHashSet();
    var foreignCount = ids.Count(x => !owned.Contains(x));
    ids = ids.Where(owned.Contains).ToList();

    var result = svc.BulkUpdateStatus(ids, status, CurrentUserId(ctx));
    var message = foreignCount == 0
        ? result.Message
        : $"{result.Message} ({foreignCount} đơn không thuộc tin này đã bị bỏ qua.)";

    return Results.Redirect($"/jobs/{id}/applicants?" + (result.Updated > 0 ? "msg=" : "err=") + Enc(message));
}).RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Mentor)).DisableAntiforgery();

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

// ATS-13/14: AI đánh giá độ phù hợp + gợi ý lộ trình
app.MapPost("/applications/{id:int}/ai-evaluate", async (int id, HttpContext ctx, IApplicationService svc, IAiService ai, IAiInputBuilder build) =>
{
    if (!svc.CanAccess(id, CurrentUserId(ctx), IsAdmin(ctx))) return Results.LocalRedirect("/denied");

    var a = svc.GetById(id);
    if (a?.CandidateProfile is null || a.Job is null)
        return Results.Redirect($"/applications/{id}?aierror=" + Enc("Không tìm thấy dữ liệu đơn."));

    try
    {
        var eval = await ai.EvaluateAsync(build.ForApplication(a, a.CandidateProfile), ctx.RequestAborted);
        svc.SaveAiEvaluation(id, eval);
        return Results.Redirect($"/applications/{id}?aiscored=1");
    }
    catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
    {
        // Người dùng bỏ trang giữa chừng — không còn ai để hiện thông báo, và đây không
        // phải lỗi nên cũng không ghi log.
        return Results.Empty;
    }
    catch (Exception ex)
    {
        return Results.Redirect($"/applications/{id}?aierror=" + Enc(SafeError(ctx, ex, $"chấm điểm đơn #{id}")));
    }
}).RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Mentor)).DisableAntiforgery();

// N1.C: sinh bộ câu hỏi phỏng vấn từ CV đã nộp + JD. Kết quả được LƯU, vì trang render
// tĩnh: sinh xong rồi redirect thì không còn gì để hiển thị, và mỗi lần mở lại trang sẽ
// tốn thêm một lượt gọi Gemini.
app.MapPost("/applications/{id:int}/ai-questions", async (int id, HttpContext ctx, IApplicationService svc, IAiService ai, IAiInputBuilder build) =>
{
    if (!svc.CanAccess(id, CurrentUserId(ctx), IsAdmin(ctx))) return Results.LocalRedirect("/denied");

    var a = svc.GetById(id);
    if (a?.CandidateProfile is null || a.Job is null)
        return Results.Redirect($"/applications/{id}?qerror=" + Enc("Không tìm thấy dữ liệu đơn."));

    try
    {
        var set = await ai.GenerateQuestionsAsync(build.ForApplication(a, a.CandidateProfile), ctx.RequestAborted);
        svc.SaveAiQuestions(id, set);
        return Results.Redirect($"/applications/{id}?qgenerated=1");
    }
    catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
    {
        return Results.Empty;
    }
    catch (Exception ex)
    {
        return Results.Redirect($"/applications/{id}?qerror=" + Enc(SafeError(ctx, ex, $"sinh câu hỏi cho đơn #{id}")));
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

        // P0-2: giá trị đó là GIỜ TƯỜNG VIỆT NAM — thứ Mentor nhìn thấy trên đồng hồ của mình.
        // Quy về UTC NGAY TẠI ĐÂY, ở biên nhận dữ liệu, để từ đó trở vào trong hệ thống chỉ
        // còn một loại thời gian. Lưu thẳng giá trị thô sẽ đẩy buổi hẹn muộn đi 7 tiếng.
        var atUtc = at == default ? default : VietnamDateHelper.ToUtcFromVietnam(at);
        schedule = new InterviewSchedule(atUtc, f["interviewLink"].ToString(), f["interviewNote"].ToString());
    }

    // P1-4: cùng lý do với ba ô lịch — ô phản hồi luôn được form gửi lên, nên chỉ đọc khi
    // Mentor thực sự chọn "Từ chối". Service cũng chỉ ghi ở đúng trạng thái đó, nên đây là
    // lớp thứ hai chứ không phải chỗ thực thi luật.
    var feedback = status == ApplicationStatus.Rejected ? f["candidateFeedback"].ToString() : null;

    var ok = svc.UpdateStatus(id, status, schedule, feedback, CurrentUserId(ctx), out var message);
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
