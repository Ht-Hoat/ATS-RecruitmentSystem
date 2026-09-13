using System.Security.Claims;

namespace ITCareerPlatform.Models;

/// <summary>
/// Metadata của một chuyên ngành IT: tên, lớp CSS và màu biểu đồ khai báo cùng một chỗ.
/// Trước đây 8 danh mục được liệt kê ở 3 nơi (Job.Categories, Ui.CategoryClass, bảng màu
/// của Dashboard) — thêm một danh mục mà quên nơi thứ ba thì biểu đồ lặng lẽ tô màu xám.
/// </summary>
public sealed record CategoryInfo(string Name, string CssClass, string Color)
{
    public static readonly IReadOnlyList<CategoryInfo> All = new[]
    {
        new CategoryInfo("Backend",  "cat cat-backend",  "#2563eb"),
        new CategoryInfo("Frontend", "cat cat-frontend", "#16a34a"),
        new CategoryInfo("Mobile",   "cat cat-mobile",   "#7c3aed"),
        new CategoryInfo("DevOps",   "cat cat-devops",   "#ea580c"),
        new CategoryInfo("Data/AI",  "cat cat-dataai",   "#dc2626"),
        new CategoryInfo("QA",       "cat cat-qa",       "#ca8a04"),
        new CategoryInfo("Design",   "cat cat-design",   "#db2777"),
        new CategoryInfo("Khác",     "cat cat-other",    "#64748b"),
    };

    public const string FallbackColor = "#64748b";

    private static readonly Dictionary<string, CategoryInfo> ByName =
        All.ToDictionary(c => c.Name, StringComparer.Ordinal);

    public static CategoryInfo? Find(string? name) =>
        name is not null && ByName.TryGetValue(name, out var c) ? c : null;
}

// Ánh xạ dữ liệu -> lớp CSS màu + định dạng hiển thị (dùng chung cho mọi trang).
public static class Ui
{
    public static string CategoryClass(string? category) =>
        CategoryInfo.Find(category)?.CssClass ?? "cat cat-other";

    public static string CategoryColor(string? category) =>
        CategoryInfo.Find(category)?.Color ?? CategoryInfo.FallbackColor;

    public static string LevelClass(string? level) => level switch
    {
        "Intern" => "lv lv-intern",
        "Junior" => "lv lv-junior",
        "Middle" => "lv lv-middle",
        "Senior" => "lv lv-senior",
        _ => "lv lv-junior"
    };

    // ATS-15: phân màu theo ngưỡng điểm AI/Final
    public static string ScoreClass(int? score) => score switch
    {
        null => "score score-none",
        > 70 => "score score-high",
        >= 40 => "score score-mid",
        _ => "score score-low"
    };

    public static string ScoreIcon(int? score) => score switch
    {
        null => "—",
        > 70 => "⭐",
        >= 40 => "⚡",
        _ => "⚠️"
    };

    // ATS-17: màu badge trạng thái đơn
    public static string StatusClass(string? status) => status switch
    {
        ApplicationStatus.Submitted => "st st-submitted",
        ApplicationStatus.Reviewing => "st st-reviewing",
        ApplicationStatus.Interview => "st st-interview",
        ApplicationStatus.Accepted => "st st-accepted",
        ApplicationStatus.Rejected => "st st-rejected",
        _ => "st"
    };

    public static string StatusColor(string? status) => status switch
    {
        ApplicationStatus.Submitted => "#3b82f6",
        ApplicationStatus.Reviewing => "#eab308",
        ApplicationStatus.Interview => "#f97316",
        ApplicationStatus.Accepted => "#22c55e",
        ApplicationStatus.Rejected => "#ef4444",
        _ => CategoryInfo.FallbackColor
    };

    public static string JobStatusLabel(string? status) =>
        status == JobStatus.Closed ? "Đã đóng" : "Đang mở";

    // ---------------------------------------------------------------
    //  Định dạng thời gian — một chỗ duy nhất thay cho 16 chuỗi format
    //  gõ tay rải khắp 9 trang (vốn đã lệch nhau).
    // ---------------------------------------------------------------
    public const string DateTimePattern = "dd/MM/yyyy HH:mm";
    public const string DatePattern = "dd/MM/yyyy";
    public const string ShortDateTimePattern = "dd/MM HH:mm";

    public static string DateTimeText(DateTime? value) =>
        value?.ToString(DateTimePattern) ?? "";

    public static string DateText(DateTime? value) =>
        value?.ToString(DatePattern) ?? "";

    public static string ShortDateTimeText(DateTime? value) =>
        value?.ToString(ShortDateTimePattern) ?? "";

    /// <summary>Giá trị cho &lt;input type="date"&gt; — luôn theo chuẩn HTML, không theo culture máy chủ.</summary>
    public static string InputDate(DateTime? value) =>
        (value ?? DateTime.Today).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Giá trị cho &lt;input type="datetime-local"&gt;. Chuẩn HTML là "yyyy-MM-ddTHH:mm";
    /// định dạng theo culture máy chủ (vi-VN cho ra "20/09/2026 14:30") bị trình duyệt bỏ
    /// qua lặng lẽ, và ô ngày hiện ra trống trơn dù dữ liệu có thật.
    /// </summary>
    public static string InputDateTimeLocal(DateTime? value) =>
        value?.ToString("yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture) ?? "";

    /// <summary>Giá trị cho &lt;input type="number"&gt;: dấu chấm thập phân, không theo culture máy chủ.</summary>
    public static string InputNumber(decimal value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Hiển thị khoảng lương, tránh "0–0 tr" khi chưa nhập.</summary>
    public static string SalaryText(decimal min, decimal max) =>
        min <= 0 && max <= 0 ? "Thỏa thuận" : $"{min:0.##}–{max:0.##} tr";
}

/// <summary>
/// Đọc danh tính người đang đăng nhập từ ClaimsPrincipal.
/// Trước đây đoạn int.TryParse(FindFirst(NameIdentifier)) được chép vào 9 component,
/// và các bản sao xử lý trường hợp lỗi khác nhau — hai bản để uid = 0 rồi vẫn đem đi
/// so quyền sở hữu.
/// </summary>
public static class CurrentUser
{
    /// <summary>Id người dùng, hoặc 0 nếu chưa đăng nhập / claim hỏng.</summary>
    public static int Id(ClaimsPrincipal? user) =>
        int.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    public static string RoleName(ClaimsPrincipal? user) =>
        user?.FindFirstValue(ClaimTypes.Role) ?? "";

    public static bool IsAdmin(ClaimsPrincipal? user) => user?.IsInRole(Roles.Admin) ?? false;

    public static bool IsStudent(ClaimsPrincipal? user) => user?.IsInRole(Roles.Student) ?? false;
}
