using System.Globalization;
using System.Text;
using ITCareerPlatform.Models;

namespace ITCareerPlatform.Services;

/// <summary>
/// P2-1: xuất danh sách ứng viên ra CSV để Mentor mở bằng Excel.
///
/// KHÔNG xuất: nội dung CV, câu hỏi phỏng vấn. Tệp CSV rời khỏi hệ thống và
/// thường được gửi qua email hoặc chat — mọi thứ trong đó coi như đã công khai trong nội bộ
/// công ty. Chỉ những cột đã hiện sẵn trên bảng danh sách mới được đi theo.
/// </summary>
public static class CsvExport
{
    private static readonly string[] Header =
    {
        "STT", "Mã đơn", "Họ tên", "Email", "Cấp bậc", "Số năm KN",
        "Tech Skills", "Có CV", "% AI", "Ngày nộp", "Trạng thái"
    };

    public static byte[] Applicants(IReadOnlyList<ApplicantListItem> items)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(",", Header.Select(Cell))).Append("\r\n");

        var i = 1;
        foreach (var a in items)
        {
            sb.Append(string.Join(",", new[]
            {
                Cell(i.ToString(CultureInfo.InvariantCulture)),
                Cell(a.Id.ToString(CultureInfo.InvariantCulture)),
                Cell(a.FullName),
                Cell(a.Email),
                Cell(a.LevelName),
                Cell(a.YearsOfExperience.ToString(CultureInfo.InvariantCulture)),
                Cell(a.TechSkillTags),
                Cell(a.HasCv ? "Có" : "Thiếu"),
                Cell(a.AiScore?.ToString(CultureInfo.InvariantCulture) ?? ""),
                // Ngày nộp quy sang giờ Việt Nam như mọi chỗ hiển thị khác (P0-2) — người
                // đọc tệp này là người Việt, không phải máy chủ.
                Cell(Ui.DateTimeText(a.AppliedAt)),
                Cell(a.Status)
            })).Append("\r\n");
            i++;
        }

        // BOM UTF-8 là thứ duy nhất làm Excel trên Windows đọc đúng tiếng Việt. Thiếu nó,
        // Excel đoán theo codepage hệ thống và "Nguyễn Văn A" thành "Nguyá»…n VÄƒn A".
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    /// <summary>
    /// Bọc một giá trị thành ô CSV, kèm chống CSV injection.
    ///
    /// Excel coi ô bắt đầu bằng = + - @ là CÔNG THỨC. Một ứng viên đặt họ tên là
    /// "=cmd|'/c calc'!A1" sẽ biến tệp Mentor vừa tải về thành một lệnh chạy trên máy họ.
    /// Thêm dấu nháy đơn ở đầu buộc Excel hiểu đó là văn bản; ký tự nháy này không hiện ra
    /// trên màn hình khi mở tệp.
    /// </summary>
    private static string Cell(string? value)
    {
        var s = value ?? "";
        if (s.Length > 0 && (s[0] is '=' or '+' or '-' or '@' or '\t' or '\r'))
            s = "'" + s;

        // Bọc nháy kép cho mọi ô, và nhân đôi nháy kép bên trong: một dấu phẩy trong Tech
        // Skills ("C#, .NET") sẽ tách thành hai cột nếu không bọc.
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
