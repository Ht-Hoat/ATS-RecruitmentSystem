# 🎨 BẢNG GIAO VIỆC GIAO DIỆN — bản `ATS-RecruitmentSystem-nvhung`
### Cập nhật 18/09/2026 · Dành cho bạn làm giao diện · Backend đã sẵn sàng, chỉ còn nối UI

> **Nguyên tắc:** mọi luồng bên dưới **backend đã chạy đúng** (có unit test xanh). Việc của bạn làm UI là **nối giao diện vào endpoint/field đã có** — không phải viết logic. Mỗi task ghi rõ: file cần sửa, phần tử UI cần thêm, backend nối vào, hành vi mong đợi, tiêu chí nghiệm thu.

---

## TRƯỚC KHI BẮT ĐẦU — trả lời 3 thắc mắc của leader

| Câu hỏi | Trả lời (đã kiểm chứng trong code) |
|---|---|
| "Đặt lại MK tự động gửi thẳng vào mail?" | **Code đã có sẵn.** Endpoint gửi email khi cấu hình SMTP; khi đó mật khẩu **KHÔNG** hiện ra màn hình nữa. Hiện tại đang hiện ra màn hình **chỉ vì chưa cấu hình SMTP**. → Làm **Task CONFIG-1** để bật. |
| "Chỗ nhập tay mật khẩu đã có chưa?" | **Backend có, UI CHƯA.** Endpoint đã đọc field `newPassword`, nhưng trang `/users` chưa có ô nhập. → Làm **Task UI-1**. |
| "Đăng ký tài khoản HR ở chỗ nào?" | **Backend có (`POST /account/register-hr`), UI CHƯA có trang.** → Làm **Task UI-2**. |

---

## TASK CONFIG-1 — Bật gửi email tự động (KHÔNG phải code, làm 5 phút)
**Không cần lập trình.** Tạo file `src/ITCareerPlatform.Web/appsettings.Development.json` (copy từ `appsettings.Development.json.example` đã có sẵn block SMTP), điền tài khoản Gmail:
- Bật xác thực 2 bước cho Gmail → tạo **App Password** 16 ký tự (https://myaccount.google.com/apppasswords).
- Điền `Smtp:User`, `Smtp:Password` (app password), `Smtp:From`.

**Nghiệm thu:** Admin bấm "Đặt lại MK" → mật khẩu tạm **đi thẳng vào Gmail** của tài khoản đó, màn hình **không** còn hiện dãy mật khẩu (chỉ báo "Đã gửi tới email…"). Nếu để trống SMTP → giữ hành vi cũ (hiện trên màn hình).

---

## NHÓM ƯU TIÊN 1 — 3 luồng mới (leader yêu cầu thấy rõ)

### TASK UI-1 — Ô nhập mật khẩu tay + nút gợi ý khi đặt lại MK
- **File:** `Components/Pages/Users/UserList.razor` — cột "Đặt lại MK", form `action="/users/{id}/reset-password"`.
- **Thêm phần tử:**
  1. Ô `<input name="newPassword" type="text" placeholder="Để trống = tự sinh mật khẩu mạnh" />` trong form (tùy chọn).
  2. Nút **"Gợi ý"**: gọi `GET /users/suggest-password` → nhận JSON `{ "password": "..." }` → điền vào ô `newPassword`.
  3. Thay `onsubmit="return confirm(...)"` **xấu** bằng **modal xác nhận đẹp** (như ảnh leader khoanh — hộp thoại tự thiết kế, không dùng `confirm()` mặc định của trình duyệt).
- **Backend nối vào (đã có):** field form `newPassword` (bỏ trống = tự sinh); endpoint `GET /users/suggest-password` (Admin, trả JSON).
- **Hành vi:** gõ tay mật khẩu → dùng đúng chuỗi đó (validate ≥8 ký tự, sai thì hiện lỗi `?err=`); bỏ trống → hệ thống tự sinh mạnh. Dù cách nào người dùng vẫn bị buộc đổi khi đăng nhập.
- **Nghiệm thu:** (a) gõ "MyPass12345" → đăng nhập được bằng đúng chuỗi đó; (b) gõ "abc" → báo lỗi "tối thiểu 8 ký tự"; (c) bấm "Gợi ý" → ô tự điền 1 chuỗi mạnh; (d) modal xác nhận hiện đẹp thay cho popup trình duyệt.
- **Ưu tiên:** Cao · **Ước lượng:** 3–4 giờ.

### TASK UI-2 — Trang đăng ký tài khoản HR + lối vào
- **File tạo mới:** `Components/Pages/Account/RegisterHr.razor`, khai báo `@page "/register-hr"`, `@layout EmptyLayout`, `@attribute [AllowAnonymous]` (giống `Register.razor`).
- **Thêm phần tử:** form `method="post" action="/account/register-hr"` gồm: Họ tên (`fullName`), Email (`email`), Mật khẩu (`password`), Xác nhận (`confirmPassword`), **Tên công ty (`companyName`)**. Hiện lỗi từ query `?error=`.
- **Sửa `Login.razor`:** thêm dòng "Bạn là nhà tuyển dụng? **Đăng ký tại đây**" → `/register-hr`; xử lý query `?hrpending=1` → banner xanh "Tài khoản HR đang chờ Admin duyệt, bạn sẽ đăng nhập được sau khi được duyệt."
- **Sửa `Register.razor`:** thêm link chéo sang `/register-hr`.
- **Backend nối vào (đã có):** `POST /account/register-hr` (đã miễn antiforgery như /account/register — **không** cần `<AntiforgeryField/>`).
- **Hành vi:** đăng ký xong → chuyển về `/login?hrpending=1`. Tài khoản tạo ra là Mentor **chờ duyệt**, chưa đăng nhập được.
- **Nghiệm thu:** vào `/register-hr` điền đủ → thấy banner chờ duyệt ở trang login; thử đăng nhập ngay → bị từ chối; sau khi Admin duyệt (UI-3) → đăng nhập được.
- **Ưu tiên:** Cao · **Ước lượng:** 3 giờ.

### TASK UI-3 — Trang Admin duyệt tài khoản HR
- **File:** tạo `Components/Pages/Users/PendingUsers.razor` (`@page "/users/pending"`, `[Authorize(Roles = Roles.Admin)]`) **hoặc** thêm 1 khối "Tài khoản chờ duyệt" ngay đầu `UserList.razor`.
- **Thêm phần tử:** bảng đổ từ `Users.GetPending()` (inject `IUserService`) — cột Họ tên / Email / Công ty / Ngày đăng ký. Mỗi dòng **2 form**:
  - `method="post" action="/users/{id}/approve"` — nút **"Duyệt"** (kèm `<AntiforgeryField />`).
  - `method="post" action="/users/{id}/reject"` — nút **"Từ chối"** (kèm `<AntiforgeryField />` + confirm).
- **Sửa `MainLayout.razor`** (khối `AuthorizeView Roles=Admin`): thêm menu **"Duyệt tài khoản HR"** → `/users/pending`, kèm **badge số lượng** = `Users.GetPending().Count` (đỏ khi > 0).
- **Backend nối vào (đã có):** `IUserService.GetPending()`, `POST /users/{id}/approve` (kích hoạt + gửi email báo nếu có SMTP), `POST /users/{id}/reject` (xóa).
- **Nghiệm thu:** có HR đăng ký → badge menu hiện số; bấm "Duyệt" → HR biến khỏi danh sách chờ, đăng nhập được; bấm "Từ chối" → tài khoản bị xóa.
- **Ưu tiên:** Cao · **Ước lượng:** 3–4 giờ.

### TASK UI-4 — Lịch sử câu hỏi phỏng vấn cho Sinh viên
- **File:** `Components/Pages/Candidate/MyApplicationDetail.razor` (đã inject `IInterviewPrepService InterviewPrep`, đã hiển thị bộ hiện tại + nút tạo).
- **Thêm phần tử:** dưới bộ câu hỏi hiện tại, khối gập **"📜 Lịch sử câu hỏi đã tạo"** đổ từ `InterviewPrep.GetHistory(AppId, uid)` — mỗi bộ hiện thời gian + danh sách câu, mỗi câu là `.Question` kèm `.Hint` (câu trả lời gợi ý ngắn). Nút "Tạo lại" đã có.
- **Backend nối vào (đã có):** `IInterviewPrepService.GetHistory(appId, candidateUserId)`.
- **Nghiệm thu:** tạo câu hỏi 2 lần (cách nhau đủ thời gian cooldown, hoặc chỉnh cooldown khi demo) → khối lịch sử hiện cả 2 bộ, mới nhất trên; mỗi câu đều có gợi ý trả lời.
- **Ưu tiên:** Trung bình · **Ước lượng:** 2 giờ.

---

##  NHÓM ƯU TIÊN 2 — 8 fix UX cũ (mang từ patch Sprint 4.5 sang nvhung)

Các fix này đã làm ở folder `integration-n1-n2` (patch `patches_sprint4_5/`) nhưng **nvhung chưa có** — copy logic sang.

| Mã | Vấn đề | Sửa gì | File |
|---|---|---|---|
| UI-5 | Cột **ID** ở `/jobs` gây "lộn xộn" | Bỏ cột ID, thêm cột "Ngày tạo ↓" | `Jobs/JobList.razor` |
| UI-6 | Nhãn "(tin của người khác)" | "👤 Tên Mentor · Chỉ xem" + `Include(j => j.CreatedBy)` trong `JobService.GetAll` | `Jobs/JobList.razor`, `Services.cs` |
| UI-7 | Đổi MK chỉ ở sidebar | Dropdown user menu (giữ nút cũ) | `Layout/MainLayout.razor` |
| UI-8 | 2 ô stat trang chủ SV không bấm được | Bọc `<a>` → `/positions`, `/my-applications` | `Dashboard.razor` |
| UI-9 | Panel "Bắt đầu tìm việc" là text-link | Lưới 4 action card | `Dashboard.razor` |
| UI-10 | Ô "Tổng đơn ứng tuyển" (Mentor) | Link → `/jobs` | `Dashboard.razor` |
| UI-11 | Panel "Công cụ Mentor" text-link | Lưới 3 action card | `Dashboard.razor` |

> CSS cho `.card-link`, `.action-grid`, `.user-menu`, `.tag-muted` nằm sẵn trong file `patches_sprint4_5/app.css` (đã gửi trước) — copy vào `wwwroot/app.css` của nvhung.

---

## 🟢 NHÓM ƯU TIÊN 3 — Admin dashboard & cột Xem JD

### TASK UI-12 — Trang chủ Admin: ô KPI link + công cụ thành card
- **File:** `Dashboard.razor` (nhánh Admin).
- 4 ô KPI: ô hợp lý cho **link** (Tài khoản→`/users`, Tin đang mở→`/jobs`, Tổng đơn→`/dashboard`).
- Khối "Công cụ quản trị hệ thống": đổi 4 dòng text-link thành **action card** (`/users`, `/jobs`, `/companies`, `/dashboard`, `/audit`).
- **Ưu tiên:** Trung bình · **Ước lượng:** 2 giờ.

### TASK UI-13 — Cột "Thao tác" ở `/jobs`: đổi tên + thêm "Xem JD"
- **File:** `Jobs/JobList.razor`.
- **Đổi tên cột** "Thao tác" → **"Ứng viên / JD"** (hoặc tách 2 cột).
- **Thêm nút "Xem JD"**. ⚠️ Trang JD hiện có (`/positions/{id}` — `JobDetail.razor`) đang `[Authorize(Roles = Student)]`. Cần **1 phần backend nhỏ**: tạo trang xem-chỉ-đọc cho Admin/Mentor, route mới `@page "/jobs/{id}/detail"`, `[Authorize(Roles = AdminOrMentor)]`, dùng `JobService.GetById`, **bỏ** phần ứng tuyển/self-check của sinh viên.
- **Ưu tiên:** Trung bình · **Ước lượng:** 3 giờ (gồm 1 route mới).
- **Ghi chú:** phần route mới này **hơi lẫn backend** — nếu muốn tôi (Claude) làm sẵn luồng route đó thì báo, bạn chỉ style lại.

---

## 📋 TÓM TẮT BACKEND ĐÃ SẴN (để người làm UI yên tâm nối vào)

| Chức năng | Endpoint / Service đã có | Field / tham số |
|---|---|---|
| Đặt lại MK (nhập tay/tự sinh) | `POST /users/{id}/reset-password` | form `newPassword` (bỏ trống=tự sinh) |
| Gợi ý mật khẩu mạnh | `GET /users/suggest-password` | trả JSON `{password}` |
| Đăng ký HR | `POST /account/register-hr` | `fullName,email,password,confirmPassword,companyName` |
| Danh sách chờ duyệt | `IUserService.GetPending()` | — |
| Duyệt HR | `POST /users/{id}/approve` | (cần `<AntiforgeryField/>`) |
| Từ chối HR | `POST /users/{id}/reject` | (cần `<AntiforgeryField/>`) |
| Lịch sử câu hỏi PV | `IInterviewPrepService.GetHistory(appId, uid)` | — |

