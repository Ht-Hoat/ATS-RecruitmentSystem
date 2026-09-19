# 🔌 TÍCH HỢP LUỒNG CHỨC NĂNG MỚI — bản `ATS-RecruitmentSystem-nvhung`
### Cập nhật 18/09/2026 · Chỉ làm LUỒNG BACKEND · Giao diện ghi ở Mục 5 để làm sau

> Theo yêu cầu: **chỉ làm luồng chức năng chạy đúng**, phần giao diện ghi ra để tự làm. Vì UI làm sau, cách kiểm chứng luồng là **chạy `dotnet test`** — tôi đã viết sẵn test cho từng luồng mới.

---

## 0. ⚠️ BẮT BUỘC LÀM TRƯỚC KHI CHẠY WEB — sinh migration

Tôi thêm **2 thay đổi CSDL**: cột `User.PendingApproval` (duyệt HR) và bảng `InterviewQuestionSnapshots` (lịch sử câu hỏi). Máy tôi không có `dotnet` nên **không tự sinh migration được** — bạn chạy đúng **3 lệnh** này trên máy (đã có VS + dotnet-ef 10.0.12 pin sẵn trong `.config`):

```bash
cd ATS-RecruitmentSystem-nvhung
dotnet tool restore
dotnet ef migrations add AddHrApprovalAndInterviewHistory --project src/ITCareerPlatform.Web
```
Rồi chạy `dotnet run` như thường — app tự `Migrate()` áp cột/bảng mới.

> **Không chạy lệnh trên** → web sẽ lỗi runtime `Invalid column name 'PendingApproval'`. 
> **`dotnet test` thì KHÔNG cần migration** (test dùng SQLite tạo bảng từ model) → chạy test được ngay để nghiệm thu luồng.

---

## 1. RESET+ · Đặt lại mật khẩu: nhập tay + gợi ý mạnh + email

**Đã có sẵn trong nvhung:** sinh mật khẩu mạnh tự động + gửi email (nếu cấu hình SMTP) + buộc đổi khi đăng nhập. **Tôi thêm:**

- Admin **gõ mật khẩu tay tùy chọn**: `IUserService.ResetPassword(..., string? manualPassword = null)`. Có nhập → dùng đúng chuỗi đó (kiểm tra ≥8 ký tự); bỏ trống → tự sinh mạnh như cũ. Dù cách nào vẫn bật `MustChangePassword`.
- **Gợi ý mật khẩu mạnh** để UI hiển thị: `IUserService.SuggestStrongPassword()` + endpoint `GET /users/suggest-password` (Admin, trả JSON `{password}`).
- Endpoint `POST /users/{id}/reset-password` giờ đọc thêm ô form **`newPassword`** (bỏ trống = tự sinh).
- **Email**: đã gửi sẵn khi có SMTP (không đổi).

**File:** `Services/Services.cs` (UserService.ResetPassword, SuggestStrongPassword), `Program.cs` (reset-password + suggest-password).
**Test:** `tests/…/ResetPasswordManualTests.cs` — 4 test (manual dùng đúng mật khẩu; manual quá ngắn bị từ chối; bỏ trống → tự sinh; suggest mạnh & khác nhau).

---

## 2. HR-REG · HR/Mentor tự đăng ký + Admin duyệt

**Kết luận sau khi xét hệ thống:** hiện admin tạo HR qua `/users/create` là an toàn nhưng là nút thắt cho một nền tảng tuyển dụng. Tôi **bổ sung luồng HR tự đăng ký có kiểm duyệt** (đúng mục tiêu "để HR tuyển dụng tốt"):

- **HR tự đăng ký:** `POST /account/register-hr` → `IUserService.RegisterHr(fullName, email, password, companyName, out error)`. Tạo tài khoản **Mentor, `IsActive=false`, `PendingApproval=true`** (kèm công ty nếu nhập). → **chưa đăng nhập được** cho tới khi duyệt (AuthService đã chặn `IsActive=false`).
- **Admin duyệt/từ chối:** `POST /users/{id}/approve` (kích hoạt + gửi email báo nếu có SMTP) · `POST /users/{id}/reject` (xóa hồ sơ chờ). Service: `ApproveUser`, `RejectUser`, `GetPending()`.
- Sau đăng ký, chuyển về `/login?hrpending=1` (UI hiện thông báo "đang chờ duyệt" — Mục 5).

**File:** `Models/Models.cs` (`User.PendingApproval`), `Services/Services.cs` (UserService), `Program.cs` (3 endpoint).
**Test:** `tests/…/HrRegistrationApprovalTests.cs` — 6 test (tạo chờ duyệt; chặn login tới khi duyệt; approve kích hoạt; reject xóa; email trùng; ghi audit).

---

## 3. HIST · Câu hỏi phỏng vấn: câu trả lời gợi ý + đổi câu + lịch sử

Trạng thái nvhung: bộ câu hỏi luyện phỏng vấn **dành cho SINH VIÊN** (`InterviewPrepService`), mỗi câu **đã có sẵn câu trả lời gợi ý ngắn** (trường `Hint`), và **đã có "tạo lại"** (giới hạn 24h/lần). **Tôi thêm phần còn thiếu:**

- **Lịch sử xem lại:** mỗi lần sinh/tạo-lại nay lưu thêm một bản chụp vào bảng `InterviewQuestionSnapshots`. 
  - `IApplicationService.GetAiQuestionHistory(appId)` → danh sách bộ câu hỏi cũ (mới nhất trước).
  - `IInterviewPrepService.GetHistory(appId, candidateUserId)` → bản có kiểm chủ đơn cho trang sinh viên.
- **Câu trả lời gợi ý ngắn:** đã có (`InterviewQuestion.Hint`) — UI chỉ cần hiển thị.
- **Đổi câu hỏi khác:** dùng nút "tạo lại" sẵn có. Lưu ý: với Gemini kết quả sẽ khác mỗi lần; với công thức offline (chưa cấu hình khóa) kết quả tất định nên có thể trùng — đây là giới hạn của bản offline.

**File:** `Models/Models.cs` (`InterviewQuestionSnapshot`), `Data/AppDbContext.cs` (DbSet), `Services/Services.cs` (SaveAiQuestions ghi thêm lịch sử + GetAiQuestionHistory), `Services/InterviewPrepService.cs` (GetHistory).
**Test:** `tests/…/InterviewQuestionHistoryTests.cs` — 5 test (lưu 2 lần giữ cả 2, mới nhất trước; bản hiện tại luôn là mới nhất; mỗi câu có Hint; lịch sử theo từng đơn; chỉ chủ đơn xem được).

> ❓ **Cần bạn quyết:** bản nvhung đã **cố ý bỏ** việc sinh câu hỏi ở phía Mentor/HR (có migration `ClearMentorGeneratedInterviewQuestions`) — giờ chỉ Sinh viên có. Nếu bạn muốn HR **cũng** có bộ câu hỏi để chuẩn bị đi hỏi, đây là quyết định thiết kế riêng — nói tôi làm thì tôi thêm luồng cho Mentor (dùng lại đúng cơ chế này). Tôi không tự thêm để không phá refactor nhóm đã cố tình làm.

---

## 4. ✅ Cách nghiệm thu (không cần UI)

```bash
cd ATS-RecruitmentSystem-nvhung
dotnet test
```
Mong đợi: toàn bộ test cũ **vẫn xanh** + 15 test mới (6 HR + 4 reset + 5 lịch sử) **pass**. 
> Tôi **không có dotnet để tự build/test** — đã rà cân bằng ngoặc và bám đúng kiểu code hiện có, nhưng bạn chạy `dotnet build` một lần để chắc chắn. Nếu có lỗi biên dịch nhỏ, gửi tôi log là tôi sửa ngay.

---

## 5. 🎨 GIAO DIỆN CẦN LÀM SAU (đúng phần bạn dặn "ghi ra để làm sau")

Backend cho tất cả mục dưới **đã sẵn sàng** — chỉ còn nối UI vào endpoint/service đã có.

### 5.1. Reset password (trang `/users` — `UserList.razor`)
- Trong form "Đặt lại MK" của mỗi dòng: thêm **ô nhập `name="newPassword"`** (bỏ trống = hệ thống tự sinh mạnh) + nút **"Gợi ý"** gọi `GET /users/suggest-password` (fetch JSON) rồi điền vào ô.
- **Thay `confirm()` xấu bằng modal đẹp** (như ảnh bạn khoanh). Sau khi đặt lại: hiện thông báo đẹp thay vì alert; mật khẩu tạm đã hiện qua cơ chế cookie `?tempPw=1` sẵn có — chỉ cần style lại khối hiển thị đó.

### 5.2. Đăng ký HR + duyệt
- **Trang mới `/register-hr`** (`RegisterHr.razor`): form fullName / email / password / confirmPassword / **companyName** → POST `/account/register-hr`. Thêm link "Bạn là nhà tuyển dụng? Đăng ký tại đây" ở `Login.razor` và `Register.razor`.
- `Login.razor`: khi query `?hrpending=1` → hiện banner "Tài khoản HR đang chờ Admin duyệt".
- **Trang duyệt cho Admin** (`Users/PendingUsers.razor` hoặc 1 khối trong `UserList.razor`): liệt kê `IUserService.GetPending()`; mỗi dòng 2 form POST `/users/{id}/approve` và `/users/{id}/reject` — **nhớ chèn `<AntiforgeryField/>`** (các form admin trong dự án đều cần token). Thêm link menu "Duyệt tài khoản HR" cho Admin trong `MainLayout.razor`.

### 5.3. Lịch sử câu hỏi phỏng vấn (trang SV `MyApplicationDetail.razor`)
- Dưới bộ câu hỏi hiện tại, thêm khối **"Lịch sử câu hỏi đã tạo"** đổ từ `InterviewPrepService.GetHistory(appId, uid)` (đã inject sẵn service ở trang này hoặc thêm `@inject`). Mỗi câu hiện `Question` + `Hint` (câu trả lời gợi ý). Nút "Tạo lại" đã có.

### 5.4. 8 fix UX cũ (chưa có trong nvhung — mang từ patch Sprint 4.5 sang)
| # | Vấn đề | Sửa | File |
|---|---|---|---|
| 1 | Cột **ID** lộn xộn ở `/jobs` | Bỏ cột ID, thêm cột "Ngày tạo ↓" | `Jobs/JobList.razor` |
| 2 | Nhãn "(tin của người khác)" | "👤 Tên Mentor · Chỉ xem" (+ `Include(CreatedBy)` trong `JobService.GetAll`) | `Jobs/JobList.razor`, `Services.cs` |
| 3 | Đổi mật khẩu chỉ ở sidebar | Dropdown user menu + giữ nút cũ | `Layout/MainLayout.razor` |
| 4 | 2 ô stat SV không bấm được | Bọc `<a>` link tới `/positions`, `/my-applications` | `Dashboard.razor` |
| 5 | Panel "Bắt đầu tìm việc" là text-link | Lưới 4 action card | `Dashboard.razor` |
| 6 | Ô Mentor "Tổng đơn ứng tuyển" | Link tới `/jobs` | `Dashboard.razor` |
| 7 | Panel "Công cụ Mentor" là text-link | Lưới 3 action card | `Dashboard.razor` |
| 8 | SV không thấy câu hỏi PV | (nvhung ĐÃ có ở SV) — chỉ bổ sung khối lịch sử mục 5.3 | `MyApplicationDetail.razor` |

> Mã CSS cho `.card-link`, `.action-grid`, `.user-menu`, `.tag-muted` đã có trong `patches_sprint4_5/app.css` gửi trước — copy các khối đó vào `wwwroot/app.css` của nvhung.

### 5.5. Trang chủ Admin (`Dashboard.razor`, nhánh Admin)
- 4 ô KPI: cho ô hợp lý **link** đến trang tương ứng (Tài khoản→`/users`, Tin đang mở→`/jobs`, Tổng đơn→`/dashboard`).
- Khối **"Công cụ quản trị hệ thống"**: đổi 4 dòng text-link thành **4 action card** (`/users`, `/jobs`, `/companies`, `/dashboard`, `/audit`).

### 5.6. Tin tuyển dụng IT — cột cuối + Xem JD (`Jobs/JobList.razor`)
- **Đổi tên cột** "Thao tác" cho đúng nội dung: gợi ý **"Ứng viên / JD"** hoặc tách 2 cột.
- **Thêm nút "Xem JD"**: trang JD hiện có là `/positions/{id}` nhưng **đang `[Authorize(Roles = Student)]`** (JobDetail.razor). Khuyến nghị flow: tạo bản xem-chỉ-đọc cho Admin/Mentor (route mới `/jobs/{id}/detail`, `[Authorize(AdminOrMentor)]`) tái dùng dữ liệu `JobService.GetById`, **không** kèm phần ứng tuyển/self-check của sinh viên. (Đây là phần UI + 1 route mới — ghi để bạn làm; nếu muốn tôi làm luồng route này thì báo.)

---

## 6. TÓM TẮT FILE ĐÃ SỬA (backend, đã ghi thẳng vào folder nvhung)
- `Models/Models.cs` — `User.PendingApproval`, entity `InterviewQuestionSnapshot`
- `Data/AppDbContext.cs` — DbSet `InterviewQuestionSnapshots`
- `Services/Services.cs` — ResetPassword (manual), SuggestStrongPassword, RegisterHr/ApproveUser/RejectUser/GetPending, GetAiQuestionHistory + SaveAiQuestions ghi lịch sử
- `Services/InterviewPrepService.cs` — GetHistory
- `Program.cs` — endpoints register-hr, approve, reject, suggest-password; reset-password nhận mật khẩu tay
- `tests/…` — 3 file test mới (15 test)

**Chưa đụng:** toàn bộ giao diện (razor) và CSS — giữ nguyên như bạn dặn; danh sách việc UI ở Mục 5.

---
*Lập 18/09/2026. Máy không có dotnet nên chưa build/test được ở đây — vui lòng chạy `dotnet test` để nghiệm thu và `dotnet ef migrations add …` (Mục 0) trước khi chạy web.*
