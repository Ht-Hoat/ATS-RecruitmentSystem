# 🧪 Hướng dẫn Kiểm thử — IT Career Platform

Tài liệu này hướng dẫn **chạy test tự động (xUnit)** và **kiểm thử tay (UAT)** cho hệ thống,
đúng theo các story kiểm thử TST-01 / TST-02 / TST-03 trong kế hoạch Sprint.

---

## PHẦN 1 — TEST TỰ ĐỘNG (xUnit + EF Core SQLite in-memory)

### 1.1. Chạy toàn bộ test

```bash
cd ITCareerPlatform
dotnet test
```

Kết quả mong đợi: **Passed! - Failed: 0**. Tổng ~30 test.

> Test dùng **SQLite in-memory**, KHÔNG cần SQL Server thật → chạy nhanh, chạy được trên CI.
> Các test AI dùng **thuật toán offline** nên KHÔNG cần API key hay mạng.

Chạy 1 nhóm test cụ thể:
```bash
dotnet test --filter "FullyQualifiedName~ApplicationServiceTests"
dotnet test --filter "FullyQualifiedName~AiServiceTests"
```

### 1.2. Bảng test đã viết (ánh xạ theo story)

| Tệp test | Story | Kịch bản kiểm thử |
|----------|-------|-------------------|
| `UserServiceTests` | ATS-01/02, EXT-01 | Đăng ký tạo SV (RoleId=3), mật khẩu **băm BCrypt** (không plaintext), email trùng, mật khẩu < 8 ký tự, đổi vai trò ghi **AuditLog**, khóa/mở khóa |
| `JobServiceTests` | ATS-04/05/07 | Tạo tin → Open, lương max<min → lỗi, lọc theo **Category**, tìm **TechStack** không phân biệt hoa thường, chỉ trả tin Open, chặn sửa tin **Closed**, chặn sửa khi **không phải Mentor chủ tin** (kể cả Admin — Admin chỉ xem, xem `AdminReadOnlyTests`) |
| `ApplicationServiceTests` | ATS-10/15/16/17 | **5 kịch bản Apply** (tin đóng / chưa hồ sơ / chưa CV / trùng / hợp lệ), **đóng băng CV vào đơn** (SV đổi CV sau không ảnh hưởng đơn cũ), đổi trạng thái ghi **lịch sử** + tạo **thông báo** (NTF-01), **HrScore không ghi đè AiScore** + FinalScore ưu tiên HrScore, xếp hạng theo % phù hợp giảm dần |
| `ProfileServiceTests` | ATS-08/09, SEC-01 | Lưu 4 trường IT, URL GitHub sai → lỗi, upload CV hợp lệ → HasCv, **chặn EICAR (mã độc)**, sai định dạng, quá 5MB |
| `AiServiceTests` | ATS-13/14 (TST-03) | Khớp tech cao → **%≥70**, không khớp → **≤40** kèm lộ trình, **tất định**, % luôn trong [0,100], trả đủ 3 mục Điểm mạnh/Thiếu sót/Lộ trình |

### 1.3. Xem độ phủ (tùy chọn)
```bash
dotnet test --collect:"XPlat Code Coverage"
```

### 1.4. Cách thêm test mới
Mở thư mục `tests/ITCareerPlatform.Tests/`. Mẫu:
```csharp
[Fact]
public void TenTest_TinhHuong_KetQua()
{
    using var t = new TestDb();                 // CSDL SQLite sạch, đã seed 3 role
    var mentor = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
    var svc = new JobService(t.Db);
    // Act ...
    // Assert.Equal(...)
}
```
`TestDb` có sẵn tiện ích: `AddUser`, `AddJob`, `AddProfile`, `NewContext()` (đọc lại từ CSDL để chắc chắn đã lưu).

---

## PHẦN 2 — KIỂM THỬ TAY / UAT (chạy app rồi thao tác)

Chạy app theo `HUONG_DAN_CHAY.md`, rồi làm theo các kịch bản dưới. Đánh dấu ✅/❌.

### TST-01 — UAT Sprint 1 (Admin + Mentor)
| # | Bước | Kết quả mong đợi |
|---|------|------------------|
| 1 | Đăng nhập admin@itcp.vn | Vào trang chủ Admin, menu có Quản lý tài khoản |
| 2 | `/users` → Tạo tài khoản | Tài khoản mới xuất hiện trong bảng |
| 3 | Đổi vai trò 1 user | Badge vai trò đổi; vào `/audit` thấy 1 dòng "Change Role" |
| 4 | Bấm Khóa 1 user | Badge chuyển "Đã khóa"; user đó đăng nhập → báo bị khóa |
| 5 | Thử tự khóa / tự đổi vai trò của mình | Bị chặn (nút ẩn, hoặc báo lỗi) |
| 6 | Đăng nhập mentor@itcp.vn → `/jobs/new` | Tạo tin IT có Category/TechStack/Level thành công |
| 7 | Đóng 1 tin → thử Sửa | Nút "Sửa" bị vô hiệu; mở lại tin thì sửa được |
| 8 | Đăng nhập SV → mở `/users` | Bị chặn về trang 403 "/denied" |

### TST-02 — UAT Sprint 2 (Sinh viên IT)
| # | Bước | Kết quả mong đợi |
|---|------|------------------|
| 1 | `/register` đăng ký SV mới (mật khẩu ≥ 8) | Đăng ký thành công → chuyển `/login` |
| 2 | Đăng nhập → `/profile` điền GitHub/LinkedIn/Portfolio/Tags | Lưu thành công; chip kỹ năng hiển thị |
| 3 | Nhập GitHub URL sai (vd facebook.com) | Báo lỗi định dạng, không lưu |
| 4 | Tải CV `.exe` hoặc > 5MB | Bị từ chối với thông báo rõ ràng |
| 5 | Tải CV `.pdf` hợp lệ | "Tải CV thành công", hiện tên tệp + nút tải lại |
| 6 | `/positions` lọc Category=Backend + tech="C#" | Chỉ hiện việc khớp; số kết quả đúng |
| 7 | Ứng tuyển khi CHƯA có CV | Bị chặn, gợi ý hoàn thiện hồ sơ |
| 8 | Ứng tuyển 1 tin hợp lệ | "Ứng tuyển thành công"; `/my-applications` có đơn |
| 9 | Ứng tuyển lại tin đó | Báo "đã ứng tuyển rồi" |
| 10 | Vào `/toolkit` tick checklist rồi F5 | Trạng thái tick được giữ (localStorage), progress bar đúng |

### TST-03 — UAT Sprint 3 (Mentor + AI)
| # | Bước | Kết quả mong đợi |
|---|------|------------------|
| 1 | Mentor mở `/jobs/{id}/applicants` | Thấy danh sách SV đã ứng tuyển, cột điểm AI |
| 2 | Đổi sắp xếp "Điểm cao nhất" | Danh sách xếp lại theo điểm giảm dần |
| 3 | Mở 1 ứng viên → "🧭 Đánh giá độ phù hợp & Gợi ý lộ trình" | Hiện **vòng tròn % phù hợp** + ✅ Điểm mạnh + ❌ Thiếu sót + 🚀 Lộ trình; lưu lại sau F5 |
| 4 | Bấm "Điều chỉnh" bỏ trống lý do | Không cho lưu (yêu cầu ≥10 ký tự) |
| 5 | Điều chỉnh % = 85 + lý do | HrScore=85%, AiScore **giữ nguyên**, hiển thị cả 2 |
| 6 | Đổi trạng thái "Đã nộp"→"Phỏng vấn" | Timeline thêm 1 mốc; SV nhận **thông báo 🔔** |
| 7 | SV đăng nhập xem chuông thông báo | Thấy thông báo đổi trạng thái, bấm vào → `/my-applications` |
| 8 | **SV mở `/my-applications` → "Xem"** | SV thấy **đánh giá % phù hợp + lộ trình học** của chính mình (ATS-14) |
| 9 | `/dashboard` | 4 thẻ số + biểu đồ tròn (Category) + cột (Status) hiển thị đúng |

### TST-03.1 — Kiểm thử AI với 5 CV mẫu
Chuẩn bị 5 hồ sơ, chạy đánh giá, ghi lại **% phù hợp** để đối chiếu:

| CV | Tech Skills hồ sơ | Ứng tuyển JD | % phù hợp kỳ vọng |
|----|-------------------|--------------|--------------|
| 1 | C#, .NET, SQL Server, Docker | Backend .NET (C#,.NET,SQL Server) | **≥ 70** ⭐ |
| 2 | JavaScript, React, TypeScript | Frontend React | **≥ 70** ⭐ |
| 3 | Python, Django, Pandas | Backend .NET (C#) | **≤ 40** ⚠️ |
| 4 | (để trống tags) | bất kỳ | ~50 trung tính |
| 5 | CV dạng ảnh scan (nếu bật Gemini) | bất kỳ | Có điểm (heuristic bổ sung bằng hồ sơ) |

> Không bật Gemini: kết quả do thuật toán offline, **tất định** → tiện đối chiếu.
> Bật Gemini: kết quả có thể khác đôi chút theo mô hình, nhưng thứ hạng vẫn hợp lý.

---

## PHẦN 3 — CI/CD (tự động test khi push)

Đã cấu hình `.github/workflows/ci.yml`: mỗi lần push/PR lên `main`/`develop`,
GitHub Actions sẽ tự `restore → build → test` trên .NET 10. PR đỏ = có test fail.
