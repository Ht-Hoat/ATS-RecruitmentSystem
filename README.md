# IT Career Platform — Hệ thống Hỗ trợ Nghề nghiệp & Tuyển dụng cho Sinh viên IT

Đồ án môn **Quản lý Dự án CNTT** — Nhóm 9.
Công nghệ: **.NET 10 (Blazor Server) · EF Core · SQL Server 2022 · Docker · (AI: Google Gemini — miễn phí)**

> Bản v2 này hoàn thiện toàn bộ 3 Sprint và bổ sung các góp ý của giảng viên:
> cân bằng velocity, thêm **Testing (xUnit)**, **Thông báo (NTF-01)**, **quét CV an toàn (SEC-01)**,
> **AI dùng Gemini miễn phí** (thay OpenAI, có phương án offline dự phòng), tách **interface** cho mọi service.

---

## 🚀 Chạy nhanh
```bash
cd ITCareerPlatform
docker compose up --build      # mở http://localhost:8080
```
Chi tiết: **HUONG_DAN_CHAY.md** · Kiểm thử: **HUONG_DAN_TEST.md**
Tài khoản demo (mật khẩu `123456`): `admin@itcp.vn`, `mentor@itcp.vn`, `lan@itcp.vn`.

---

## 🏗️ Kiến trúc & cấu trúc thư mục
```
ITCareerPlatform/
├─ src/ITCareerPlatform.Web/        # Ứng dụng Blazor Server (.NET 10)
│  ├─ Models/Models.cs              # 8 entity + hằng Roles/ApplicationStatus
│  ├─ Data/AppDbContext.cs          # EF Core mapping + seed 3 vai trò
│  ├─ Data/SeedData.cs              # dữ liệu mẫu IT (users, jobs, profiles, apps)
│  ├─ Services/Services.cs          # 6 service + interface (nghiệp vụ)
│  ├─ Services/AiService.cs         # Gemini + heuristic offline (ATS-13/14)
│  ├─ Services/CvUtilities.cs       # CvScanner (SEC-01) + CvTextExtractor
│  ├─ Components/Pages/...          # Giao diện Blazor theo từng vai trò
│  ├─ Program.cs                    # DI, auth cookie, endpoints
│  ├─ Dockerfile                    # image .NET 10
│  └─ appsettings*.json
├─ tests/ITCareerPlatform.Tests/    # xUnit + EF Core SQLite in-memory (~30 test)
├─ docker-compose.yml               # App + SQL Server 2022
└─ .github/workflows/ci.yml         # CI: restore → build → test
```

Tầng: **Blazor UI (SSR) → Services (interface) → EF Core → SQL Server**.
Xác thực **Cookie**, phân quyền theo **Role** (`Admin` / `Mentor` / `SinhVienIT`).

---

## 🗺️ Ánh xạ Story → Mã nguồn

| Story | Chức năng | Nơi cài đặt chính |
|-------|-----------|-------------------|
| ATS-01/02 | Quản lý tài khoản + phân quyền + AuditLog | `UserService`, `Pages/Users/*`, `Program.cs` |
| ATS-03 / EXT-01 | Đăng nhập / Đăng ký SV IT | `AuthService`, `UserService.Register`, `Login/Register.razor` |
| ATS-04/05/06 | CRUD tin việc IT (Category, TechStack, Level) | `JobService`, `Pages/Jobs/*` |
| ATS-07 | Xem + lọc việc IT | `JobService.Filter`, `Positions.razor` |
| ATS-08/09 | Hồ sơ IT (GitHub/LinkedIn/Portfolio/Tags) + CV | `ProfileService`, `Profile.razor` |
| ATS-10 | Ứng tuyển (3 lớp kiểm tra) + **đóng băng CV vào đơn** (snapshot byte[]) | `ApplicationService.Apply` |
| ATS-11/12 | Mentor xem DS + chi tiết hồ sơ + tải CV | `JobApplicants.razor`, `ApplicantDetail.razor` |
| ATS-13/14 | AI **đánh giá độ phù hợp + gợi ý lộ trình** (Mentor & SV cùng xem) | `GeminiAiService`, `AiEvaluationCard`, `MyApplicationDetail.razor` |
| ATS-15 | Xếp hạng + phân màu theo **% phù hợp** | `JobApplicants.razor`, `Ui.ScoreClass` |
| ATS-16 | Human-in-the-loop: điểm AI chỉ để tham khảo/xếp hạng; quyết định cuối (mời phỏng vấn / từ chối / nhận) do Mentor chọn | `ApplicantDetail.razor` (thẻ quyết định), `ApplicationService.UpdateStatus` |
| ATS-17 | Trạng thái 5 bước + lịch sử timeline | `ApplicationService.UpdateStatus`, `ApplicationStatusHistory` |
| ATS-18 | Dashboard biểu đồ | `DashboardAnalytics.razor` (Chart.js) |
| **NTF-01** | Thông báo cho SV khi đổi trạng thái | `NotificationService`, chuông ở `MainLayout` |
| **SEC-01** | Quét CV an toàn (chặn exe/EICAR) | `CvScanner` |
| **TST-01/02/03** | Kiểm thử tự động | `tests/ITCareerPlatform.Tests/*` |

---

## 🤖 Về AI — "Đánh giá độ phù hợp & Gợi ý lộ trình" (Gemini miễn phí + dự phòng)
AI đóng vai **cố vấn hướng nghiệp**: với mỗi đơn, trả về **% phù hợp** + **✅ Điểm mạnh** + **❌ Thiếu sót** + **🚀 Lộ trình tự học ngắn hạn**. Mentor xem để lọc/xếp hạng; **Sinh viên cũng xem được đánh giá của chính mình** ở trang "Đơn của tôi" để biết cần học thêm gì.
- Cấu hình `Gemini:ApiKey` (lấy free tại https://aistudio.google.com/apikey) để dùng AI thật.
- **Không có key vẫn chạy**: `GeminiAiService` tự chuyển sang **heuristic offline**
  (theo tỉ lệ khớp Tech Stack) — cũng là phương án dự phòng khi Gemini lỗi/hết hạn mức (rủi ro R1/R4).
- Prompt có chỉ dẫn **bỏ qua chỉ thị ẩn trong CV** để chống prompt injection (rủi ro R3).

## 🔒 Bảo mật
- Mật khẩu băm **BCrypt**. API key để trong `appsettings.Development.json` (đã `.gitignore`).
- CV bị **quét** trước khi lưu: chặn tệp thực thi (MZ/ELF) và chữ ký thử virus EICAR.
- Phân quyền endpoint bằng `RequireAuthorization(RequireRole(...))` + `[Authorize]` trên trang.
- **Mentor chỉ xem/thao tác ứng viên của tin do chính mình tạo.** Admin **chỉ xem** phần tuyển dụng (mọi tin, ứng viên, thống kê) để giám sát; đăng/sửa/đóng tin, chấm AI, mời phỏng vấn, từ chối, nhận, xuất CSV đều chỉ Mentor tạo tin làm được — kiểm ở tầng service (`CanView` / `CanModify`), không chỉ ẩn nút.

## 🩹 Các điểm nghẽn đã xử lý (bản review)
- **Hiệu năng:** danh sách ứng viên/đơn dùng **projection DTO** (`ApplicantListItem`, `MyApplicationItem`) nên KHÔNG kéo `byte[]` CV về khi chỉ hiển thị bảng; số ứng viên đếm 1 lần bằng `CountAllByJob()` (bỏ N+1).
- **Bảo mật:** vá lỗ hổng leo quyền ngang — Mentor không truy cập được đơn của tin người khác (trang + các endpoint: tải CV, đánh giá AI, đổi trạng thái).
- **Nghiệp vụ:** chặn ứng tuyển khi **quá hạn nộp** (`Deadline`), ẩn tin hết hạn khỏi `/positions`; bắt `DbUpdateException` khi 2 request nộp trùng cùng lúc → báo thân thiện thay vì lỗi 500; thêm trang lỗi chung `/error`.

## 📌 Hạn chế đã biết & hướng phát triển
Các điểm sau **cố ý để lại** cho phù hợp phạm vi đồ án (không làm phức tạp hệ thống), ghi nhận để nâng cấp sau:
- **CSRF:** các form SSR đang `DisableAntiforgery()` — nên bật lại antiforgery token khi lên môi trường thật.
- **Chống brute-force đăng nhập** (khóa tạm sau N lần sai) và **xác thực email** khi đăng ký.
- **EF Migrations:** hiện dùng `EnsureCreated()`; nên chuyển hẳn sang migration để nâng cấp schema mà không mất dữ liệu.
- **Lưu CV ra blob storage** thay vì `varbinary(max)` trong DB khi dữ liệu lớn.
- **Phân trang** cho các danh sách; **quét virus thật** (ClamAV) thay heuristic; **thông báo/đồng ý** xử lý dữ liệu cá nhân khi gửi CV cho AI (đã có dòng lưu ý trên UI).
