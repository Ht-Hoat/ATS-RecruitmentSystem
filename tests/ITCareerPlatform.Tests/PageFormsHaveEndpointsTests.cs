using System.Text.RegularExpressions;
using Xunit;

namespace ITCareerPlatform.Tests;

/// <summary>
/// Trang render tĩnh gửi form bằng thẻ HTML thuần tới các endpoint MapPost trong Program.cs.
/// Hai phía không có gì ràng buộc nhau lúc biên dịch: xóa nhầm một MapPost (đã xảy ra với
/// /companies/save và /companies/assign ở P1-3) thì build vẫn xanh, test service vẫn xanh,
/// còn trang đó thì hỏng hẳn khi chạy thật. Test này đọc mã nguồn và đối chiếu hai phía.
/// </summary>
public class PageFormsHaveEndpointsTests
{
    private static readonly Regex FormTag = new(@"<form\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex MethodAttr = new(@"\bmethod\s*=\s*""post""", RegexOptions.IgnoreCase);
    private static readonly Regex ActionAttr = new(@"\baction\s*=\s*""(?<v>(?:@\(\$""[^""]*""\))|[^""]*)""", RegexOptions.IgnoreCase);
    private static readonly Regex MapPost = new(@"app\.MapPost\(\s*""(?<route>[^""]+)""");
    private static readonly Regex RouteParam = new(@"\{[^}]*\}");

    private static string WebProjectDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "ITCareerPlatform.Web", "Program.cs")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Không tìm thấy thư mục gốc repo (src/ITCareerPlatform.Web/Program.cs).");
        return Path.Combine(dir!.FullName, "src", "ITCareerPlatform.Web");
    }

    /// <summary>"/applications/{app.Id}/cv" và "/applications/{id:int}/cv" cùng thành "/applications/{}/cv".</summary>
    private static string Normalize(string route) =>
        RouteParam.Replace(route, "{}").TrimEnd('/').ToLowerInvariant();

    private static IEnumerable<(string File, string Action)> PostForms(string webDir)
    {
        foreach (var file in Directory.EnumerateFiles(Path.Combine(webDir, "Components"), "*.razor", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (Match tag in FormTag.Matches(text))
            {
                if (!MethodAttr.IsMatch(tag.Value)) continue;
                var action = ActionAttr.Match(tag.Value);
                Assert.True(action.Success, $"{Path.GetFileName(file)}: form POST không có action — sẽ gửi ngược về chính trang, mà trang tĩnh không xử lý POST.");
                var value = action.Groups["v"].Value;
                if (value.StartsWith("@($\"")) value = value[4..^2];   // @($"/x/{a.Id}/y") → /x/{a.Id}/y
                yield return (Path.GetFileName(file), value);
            }
        }
    }

    [Fact]
    public void EveryPostFormInComponents_HasAMatchingMapPost()
    {
        var webDir = WebProjectDir();
        var routes = MapPost.Matches(File.ReadAllText(Path.Combine(webDir, "Program.cs")))
                            .Select(m => Normalize(m.Groups["route"].Value))
                            .ToHashSet();
        var forms = PostForms(webDir).ToList();

        Assert.NotEmpty(forms);
        var orphans = forms.Where(f => !routes.Contains(Normalize(f.Action)))
                           .Select(f => $"{f.File}: {f.Action}")
                           .ToList();
        Assert.True(orphans.Count == 0, "Form POST không có endpoint nào nhận:\n" + string.Join("\n", orphans));
    }

    /// <summary>
    /// Mỗi form POST phải mang token chống giả mạo BÊN TRONG chính nó. Đếm số lượng trong cả
    /// tệp là không đủ: nút Đăng xuất từng có ô token nằm ngay SAU &lt;/form&gt; —
    /// số lượng vẫn khớp, nhưng form gửi đi không có token và middleware chặn lại, tức là
    /// không đăng xuất được.
    /// </summary>
    [Fact]
    public void EveryPostForm_CarriesItsOwnAntiforgeryField()
    {
        var missing = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(WebProjectDir(), "Components"), "*.razor", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (Match tag in FormTag.Matches(text))
            {
                if (!MethodAttr.IsMatch(tag.Value)) continue;
                var close = text.IndexOf("</form>", tag.Index, StringComparison.OrdinalIgnoreCase);
                var body = close < 0 ? "" : text[tag.Index..close];
                if (!body.Contains("<AntiforgeryToken"))
                    missing.Add($"{Path.GetFileName(file)}: {ActionAttr.Match(tag.Value).Groups["v"].Value}");
            }
        }
        Assert.True(missing.Count == 0, "Form POST không mang token bên trong:\n" + string.Join("\n", missing));
    }

    /// <summary>Chính hai đường đã từng bị xóa nhầm — khóa riêng để thông báo lỗi nói thẳng vào chuyện.</summary>
    [Theory]
    [InlineData("/companies/save")]
    [InlineData("/companies/assign/{userId:int}")]
    public void CompanyEndpoints_AreMapped(string route)
    {
        var program = File.ReadAllText(Path.Combine(WebProjectDir(), "Program.cs"));
        Assert.Contains($"app.MapPost(\"{route}\"", program);
    }
}
