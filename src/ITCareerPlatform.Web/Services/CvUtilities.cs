using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace ITCareerPlatform.Services;

// =====================================================================
//  SEC-01: Quét tệp CV trước khi lưu (validate cơ bản, không cần daemon ngoài).
//  Chặn: sai định dạng, sai magic bytes, quá 5MB, chuỗi thử virus EICAR,
//  tệp thực thi (EXE/ELF), và .docx nén bung quá lớn (zip bomb).
// =====================================================================
public static class CvScanner
{
    public const long MaxBytes = 5 * 1024 * 1024;

    /// <summary>Trần cho tổng kích thước sau khi giải nén một .docx. Vượt mức này là zip bomb.</summary>
    public const long MaxUncompressedBytes = 40 * 1024 * 1024;

    // Chuỗi thử virus chuẩn EICAR (an toàn, dùng để kiểm thử antivirus)
    private const string Eicar = @"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR";

    public static (bool ok, string? error) Scan(byte[] data, string fileName)
    {
        if (data is null || data.Length == 0)
            return (false, "Tệp CV rỗng.");

        var ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        if (ext != ".pdf" && ext != ".docx")
            return (false, "Chỉ chấp nhận tệp PDF hoặc DOCX.");

        if (data.Length > MaxBytes)
            return (false, "Kích thước tệp vượt quá 5MB cho phép.");

        // Quét chữ ký mã độc TRƯỚC khi xét định dạng: một tệp chứa virus phải được
        // báo là virus, không phải "PDF hỏng".
        var head = Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 4096));
        if (head.Contains(Eicar, StringComparison.Ordinal))
            return (false, "Phát hiện tệp chứa mã độc (EICAR) — bị từ chối.");

        // Chặn tệp thực thi giả mạo đuôi
        if (StartsWith(data, 0x4D, 0x5A))                          // 'MZ' (Windows PE)
            return (false, "Tệp có dấu hiệu là chương trình thực thi (.exe) — bị từ chối.");
        if (StartsWith(data, 0x7F, 0x45, 0x4C, 0x46))              // ELF
            return (false, "Tệp có dấu hiệu là chương trình thực thi (ELF) — bị từ chối.");

        // Đuôi tệp phải khớp nội dung thật. Trước đây chỉ đuôi được kiểm tra, nên một tệp
        // ZIP đặt tên .pdf vẫn lọt và sau đó được xử lý theo nhánh DOCX.
        if (ext == ".pdf" && !IsPdf(data))
            return (false, "Nội dung tệp không phải PDF hợp lệ (thiếu chữ ký %PDF).");
        if (ext == ".docx" && !IsZip(data))
            return (false, "Nội dung tệp không phải DOCX hợp lệ (thiếu chữ ký ZIP).");

        // Zip bomb: .docx khai báo kích thước bung rất lớn so với 5MB nén.
        if (ext == ".docx" || IsZip(data))
        {
            var (zipOk, zipErr) = CheckArchiveExpansion(data);
            if (!zipOk) return (false, zipErr);
        }

        return (true, null);
    }

    private static bool StartsWith(byte[] data, params byte[] sig)
    {
        if (data.Length < sig.Length) return false;
        for (var i = 0; i < sig.Length; i++)
            if (data[i] != sig[i]) return false;
        return true;
    }

    internal static bool IsPdf(byte[] data) => StartsWith(data, 0x25, 0x50, 0x44, 0x46);  // %PDF
    internal static bool IsZip(byte[] data) => StartsWith(data, 0x50, 0x4B);              // PK

    /// <summary>
    /// Cộng kích thước bung khai báo trong ZIP directory. Một .docx bình thường bung ra
    /// vài MB; một zip bomb 5MB khai báo hàng GB. Kiểm tra trước khi đọc bất kỳ byte nào.
    /// </summary>
    private static (bool ok, string? error) CheckArchiveExpansion(byte[] data)
    {
        try
        {
            using var ms = new MemoryStream(data, writable: false);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            long total = 0;
            foreach (var entry in zip.Entries)
            {
                total += entry.Length;
                if (total > MaxUncompressedBytes)
                    return (false, "Tệp nén bung ra quá lớn — bị từ chối vì nghi ngờ zip bomb.");
            }
            return (true, null);
        }
        catch (InvalidDataException)
        {
            return (false, "Tệp DOCX hỏng hoặc không đọc được.");
        }
    }
}

// =====================================================================
//  Trích xuất text từ CV (PDF/DOCX) — phục vụ đánh giá độ phù hợp.
//
//  PDF được đọc bằng PdfPig: nội dung trang PDF nằm trong stream đã nén
//  FlateDecode, nên cách lọc "byte in được" trước đây chỉ trả về rác của
//  bộ nén (obj/endobj/stream) chứ không phải chữ trong CV, đồng thời xóa
//  sạch dấu tiếng Việt vì mọi byte UTF-8 có dấu đều ≥ 0xC0.
// =====================================================================
public static class CvTextExtractor
{
    /// <summary>Giới hạn số trang PDF đọc — CV dài bất thường không được chiếm thời gian request.</summary>
    private const int MaxPdfPages = 30;

    /// <summary>Trần ký tự đọc từ document.xml của DOCX (song song với trần zip bomb ở CvScanner).</summary>
    private const int MaxDocxChars = 2_000_000;

    public static string Extract(byte[]? data, string? fileName)
    {
        if (data is null || data.Length == 0) return "";
        var ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();

        try
        {
            // Nhận dạng theo nội dung thật trước, đuôi tệp chỉ là gợi ý.
            if (CvScanner.IsPdf(data)) return ExtractPdf(data);
            if (CvScanner.IsZip(data) || ext == ".docx") return ExtractDocx(data);
            return ExtractPlainText(data);
        }
        catch (Exception)
        {
            // Không đọc được nội dung có cấu trúc thì trả chuỗi rỗng, để phía gọi
            // dùng thông tin hồ sơ có cấu trúc. Trả rác về còn tệ hơn không trả gì:
            // rác từng làm điểm phù hợp tăng lên vì khớp nhầm token.
            return "";
        }
    }

    private static string ExtractPdf(byte[] data)
    {
        using var doc = PdfDocument.Open(data);
        var sb = new StringBuilder();
        var pages = 0;
        foreach (var page in doc.GetPages())
        {
            sb.Append(page.Text).Append('\n');
            if (++pages >= MaxPdfPages) break;
        }
        return Collapse(sb.ToString());
    }

    private static string ExtractDocx(byte[] data)
    {
        using var ms = new MemoryStream(data, writable: false);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = zip.GetEntry("word/document.xml");
        if (entry is null) return "";

        // Đọc có trần thay vì ReadToEnd(): một .docx 5MB có thể khai báo bung ra hàng GB,
        // và ReadToEnd() sẽ dựng cả chuỗi đó trên luồng đang phục vụ request.
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        var buffer = new char[8192];
        var sb = new StringBuilder();
        int read;
        while (sb.Length < MaxDocxChars && (read = reader.Read(buffer, 0, buffer.Length)) > 0)
            sb.Append(buffer, 0, read);

        // Bỏ thẻ XML, giữ lại nội dung văn bản
        var text = Regex.Replace(sb.ToString(), "<[^>]+>", " ", RegexOptions.None, TimeSpan.FromSeconds(5));
        return Collapse(text);
    }

    /// <summary>Tệp text thuần: giải mã UTF-8 (giữ dấu tiếng Việt) thay vì lọc byte ASCII.</summary>
    private static string ExtractPlainText(byte[] data)
    {
        var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false)
            .GetString(data);
        return Collapse(text);
    }

    private static string Collapse(string text) =>
        Regex.Replace(text, @"\s+", " ", RegexOptions.None, TimeSpan.FromSeconds(5)).Trim();
}
