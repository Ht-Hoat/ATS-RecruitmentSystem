# Sprint 4 UI implementation report

## 1. Các UI đã hoàn thành

| ID | Chức năng | File | Status |
| --- | --- | --- | --- |
| UI-1 | Admin reset password | `UserList.razor` | Blocked — backend endpoint absent |
| UI-2 | HR Register | `Account/RegisterHr.razor` | Partial — page/form and entry links added; submission disabled because endpoint absent |
| UI-3 | Pending HR | `Users/PendingUsers.razor`, `Layout/MainLayout.razor` | Partial — Admin route/navigation and explicit empty/unavailable state added; data/actions blocked by backend |
| UI-4 | Lịch sử câu hỏi AI | `Candidate/MyApplicationDetail.razor` | Partial — collapsible section added; history data blocked by backend service absent |
| UI-5 | Job List | `Jobs/JobList.razor` | Done — removed ID column, added created date; existing descending service order retained |
| UI-6 | Mentor tạo Job | `Jobs/JobList.razor` | Done — renders `👤 Tên Mentor · Chỉ xem` from existing user service data |
| UI-7 | Đổi mật khẩu | `Layout/MainLayout.razor` | Blocked — no change-password route/endpoint exists |
| UI-8 | Student dashboard links | `Dashboard.razor` | Done |
| UI-9 | Student action cards | `Dashboard.razor` | Done |
| UI-10 | Mentor dashboard KPI link | `Dashboard.razor` | Done |
| UI-11 | Mentor tool cards | `Dashboard.razor` | Done |
| UI-12 | Admin dashboard cards | `Dashboard.razor` | Done |
| UI-13 | Admin/Mentor read-only Job Detail | `Jobs/JobDetail.razor`, `Jobs/JobList.razor` | Done |

## 2. File đã thay đổi

- `src/ITCareerPlatform.Web/Components/Layout/MainLayout.razor`
- `src/ITCareerPlatform.Web/Components/Pages/Account/RegisterHr.razor`
- `src/ITCareerPlatform.Web/Components/Pages/Candidate/MyApplicationDetail.razor`
- `src/ITCareerPlatform.Web/Components/Pages/Dashboard.razor`
- `src/ITCareerPlatform.Web/Components/Pages/Jobs/JobDetail.razor`
- `src/ITCareerPlatform.Web/Components/Pages/Jobs/JobList.razor`
- `src/ITCareerPlatform.Web/Components/Pages/Login.razor`
- `src/ITCareerPlatform.Web/Components/Pages/Register.razor`
- `src/ITCareerPlatform.Web/Components/Pages/Users/PendingUsers.razor`
- `src/ITCareerPlatform.Web/wwwroot/app.css`
- `UI_SPRINT4_IMPLEMENTATION_REPORT.md`

## 3. Backend/database changes after diagnosis

Ban đầu không có thay đổi backend theo yêu cầu Sprint 4. Sau khi được cấp quyền rõ ràng để khắc phục lỗi chạy web, đã bổ sung:

- `src/ITCareerPlatform.Web/Migrations/20260919154000_AddEmploymentTypeToJobs.cs`: migration tương thích database cũ, thêm `Jobs.EmploymentType` với mặc định `Onsite` nếu cột chưa tồn tại.
- `src/ITCareerPlatform.Web/Program.cs`: chạy migration sau `EnsureCreated` để schema database cũ và mới đều nhận được migration tăng dần.

Migration đã được áp dụng cho database cục bộ `ITCareerPlatform`.

## 4. Build/Test

- `dotnet build`: PASS (2 cảnh báo NU1903 có sẵn cho `SQLitePCLRaw.lib.e_sqlite3` 2.1.11)
- `dotnet test --no-build`: PASS — 59/59 tests

Sau cập nhật database:

- `dotnet build`: PASS
- `dotnet ef database update`: PASS
- `dotnet test --no-build`: PASS — 59/59 tests

## 5. Vấn đề phát hiện

### UI-1 — reset password

- **Backend dependency:** `GET /users/suggest-password`, `POST /users/{id}/reset-password`.
- **Expected:** gợi ý mật khẩu và reset bằng mật khẩu thủ công/tự sinh.
- **Actual:** không có endpoint hoặc method tương ứng trong `Program.cs` / `IUserService`.
- Không sửa backend theo yêu cầu.

### UI-2/UI-3 — HR registration and approval

- **Backend dependency:** `POST /account/register-hr`, nguồn dữ liệu pending, `POST /users/{id}/approve`, `POST /users/{id}/reject`.
- **Expected:** HR đăng ký, chờ duyệt và Admin approve/reject.
- **Actual:** các endpoint, trạng thái pending và dữ liệu company không tồn tại trong backend/model hiện tại.
- Không sửa backend theo yêu cầu.

### UI-4 — interview question history

- **Backend dependency:** `InterviewPrep.GetHistory(AppId, uid)`.
- **Expected:** snapshots với thời gian, câu hỏi và hint.
- **Actual:** `InterviewPrep`, snapshot model và history API/service không tồn tại.
- Không sửa backend theo yêu cầu.

### UI-7 — change password

- **Backend dependency:** route/page change-password và endpoint xử lý.
- **Expected:** link trong user menu đến chức năng đổi mật khẩu đang có.
- **Actual:** project hiện không có route hay endpoint change password để UI liên kết an toàn.
- Không sửa backend theo yêu cầu.

## 6. Danh sách UI chưa hoàn thành

- UI-1, UI-7: chưa thể triển khai hành vi vì thiếu endpoint/service backend.
- UI-2, UI-3, UI-4: đã có route/section an toàn, nhưng không thể thực hiện luồng nghiệp vụ hoặc hiển thị dữ liệu thật vì thiếu dependency backend nêu trên.

Tài liệu `GIAO_VIEC_GIAO_DIEN.md` và `TICH_HOP_LUONG_MOI.md` không có trong working tree tại thời điểm thực hiện; yêu cầu trong prompt được dùng để đối chiếu với code hiện hữu.
