using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace ITCareerPlatform.Services;

// =====================================================================
//  SEC-01: Quét tệp CV trước khi lưu (validate cơ bản, không cần daemon ngoài).
//  Chặn: sai định dạng, quá 5MB, chuỗi thử virus EICAR, tệp thực thi (EXE/ELF).
// =====================================================================
public static class CvScanner
{
    private const long MaxBytes = 5 * 1024 * 1024;

    // Chuỗi thử virus chuẩn EICAR (an toàn, dùng để kiểm thử antivirus)
    private const string Eicar = @"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR";

    public static (bool ok, string? error) Scan(byte[] data, string fileName)
    {
        if (data is null || data.Length == 0)
            return (false, "Tệp CV rỗng.");

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext != ".pdf" && ext != ".docx")
            return (false, "Chỉ chấp nhận tệp PDF hoặc DOCX.");

        if (data.Length > MaxBytes)
            return (false, "Kích thước tệp vượt quá 5MB cho phép.");

        // Chặn tệp thực thi giả mạo đuôi
        if (data.Length >= 2 && data[0] == 0x4D && data[1] == 0x5A)      // 'MZ' (Windows PE)
            return (false, "Tệp có dấu hiệu là chương trình thực thi (.exe) — bị từ chối.");
        if (data.Length >= 4 && data[0] == 0x7F && data[1] == 0x45 && data[2] == 0x4C && data[3] == 0x46) // ELF
            return (false, "Tệp có dấu hiệu là chương trình thực thi (ELF) — bị từ chối.");

        // Quét chữ ký EICAR trong phần đầu tệp
        var head = Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 4096));
        if (head.Contains(Eicar, StringComparison.Ordinal))
            return (false, "Phát hiện tệp chứa mã độc (EICAR) — bị từ chối.");

        return (true, null);
    }
}

// =====================================================================
//  Trích xuất text thô từ CV (PDF/DOCX) — phục vụ AI chấm điểm.
//  Không phụ thuộc gói ngoài: DOCX đọc word/document.xml; còn lại lọc ký tự in được.
//  Nếu trích xuất được quá ít, phía gọi sẽ bổ sung bằng thông tin hồ sơ có cấu trúc.
// =====================================================================
public static class CvTextExtractor
{
    public static string Extract(byte[]? data, string? fileName)
    {
        if (data is null || data.Length == 0) return "";
        var ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        try
        {
            if (ext == ".docx" || (data.Length > 1 && data[0] == 0x50 && data[1] == 0x4B)) // 'PK' zip
                return ExtractDocx(data);
            return ExtractPrintable(data);
        }
        catch
        {
            return ExtractPrintable(data);
        }
    }

    private static string ExtractDocx(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = zip.GetEntry("word/document.xml");
        if (entry is null) return "";
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        var xml = reader.ReadToEnd();
        // Bỏ thẻ XML, giữ lại nội dung văn bản
        var text = Regex.Replace(xml, "<[^>]+>", " ");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static string ExtractPrintable(byte[] data)
    {
        var sb = new StringBuilder();
        foreach (var b in data)
        {
            if (b is (>= 32 and < 127) or 10 or 13 or 9) sb.Append((char)b);
        }
        var text = Regex.Replace(sb.ToString(), @"[^\x20-\x7E\s]", " ");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }
}
