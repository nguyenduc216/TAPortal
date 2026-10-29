# UI/UX redesign theo mockup — TAPortal Admin + T.A Customer Portal

- FEATURE_ID `TAPORTAL-SYSTEM-AUDIT` · CHANGE_ID `TAPORTAL-SYSTEM-AUDIT-CHG-20261029T151702Z-ABDD41`
- Source SHA `70917eb9170426d8843eaed3dd3c6b0822b5800e` (branch `feature/customer-portal-mvp01`, base `d4c5332`)
- Ảnh bằng chứng: `docs/audits/ui-redesign-TAPORTAL-SYSTEM-AUDIT-CHG-20261029T151702Z-ABDD41/` (`before-*` = mã trước thay đổi, `after-*` = sau)

## 1. Cách kiểm chứng

- **Không dùng SQL.** `tests/TACustomerPortal.Tests/VisualSnapshotTests.cs` render từng Razor view thật (kèm layout) bằng model mẫu trong bộ nhớ, ghi ra HTML tĩnh. Dữ liệu mẫu chỉ dùng cho ảnh chụp, tên miền `.example`.
- **Chụp ảnh:** Chrome headless ở 1440px (desktop) và 1024px (tablet). Mobile 390px được chụp qua iframe rộng đúng 390px, vì Chrome headless không thu cửa sổ dưới khoảng 500px.
- **Biến thể:** drawer mobile đang mở; sidebar thu gọn trên desktop; CDN bị chặn (chỉ áp dụng cho mã cũ).
- **Điều kiện mạng:** HTML sau thay đổi không chứa URL ngoài nào, nên ảnh "after" được chụp hoàn toàn offline.
- **Phạm vi chưa kiểm chứng:** chưa kiểm tra trên trình duyệt thật của người dùng, IIS hay dữ liệu thật của TAPortalSys (**VISUAL_NOT_VERIFIED trên môi trường Production**). Hành vi click (mở dropdown, drawer, lọc bảng) chưa được tự động hóa; ảnh drawer mở dựng bằng cách thêm class trạng thái.

## 2. Lỗi UI/UX phát hiện (audit)

| ID | Mức | Vấn đề | Nguyên nhân có bằng chứng | File | Trạng thái |
|---|---|---|---|---|---|
| UI-01 | HIGH | Sidebar/menu mất icon, mất style; trên mobile/tablet menu không mở được | Toàn bộ Tabler CSS/JS và icon webfont tải từ `cdn.jsdelivr.net`. Ảnh `before-admin-customers-offline@1440.png` (CDN bị chặn): menu thành danh sách link gạch chân, không icon. Ngay cả khi có mạng, ảnh headless cũ (`before-admin-customers@1440.png`) cũng không có icon nào; nguyên nhân cụ thể trong môi trường đó không được kết luận | `Views/Shared/_Layout.cshtml` (2 app) | FIXED: Tabler bundle local + icon SVG inline |
| UI-02 | HIGH | Mobile: vùng navy trống rất lớn (logo phóng to khoảng 430px), tiêu đề bị bóp, bảng tràn và cột Thao tác bị cắt | `navbar-expand-lg` biến sidebar thành thanh trên cùng; ảnh logo không giới hạn kích thước ở trạng thái thu gọn; bảng chỉ cuộn ngang (`before-admin-customers@390.png`) | `_Layout.cshtml`, `ta-portal.css`, các view danh sách | FIXED: drawer off-canvas; bảng chuyển thành thẻ ở < 768px |
| UI-03 | MEDIUM | Badge "Sắp có" bị cắt ở mép sidebar | Badge `ms-auto` vượt chiều rộng 15rem | `_Layout.cshtml` | FIXED |
| UI-04 | MEDIUM | Tablet 1024px: email bị ngắt từng ký tự, cột Thao tác dễ bị đẩy ra ngoài | `text-break` trên cột email; không có cột cố định | `Customers/Index`, `Users/Index`, `CustomerPortalAccounts/Index` | FIXED: `ta-nowrap` + cột Thao tác sticky |
| UI-05 | MEDIUM | Menu active chỉ đúng với URL Index; trang con (Sửa, Vai trò) không đánh dấu menu | JS so sánh nguyên path | `wwwroot/js/ta-portal.js` | FIXED: đánh dấu ở server theo controller, `aria-current="page"` |
| UI-06 | MEDIUM | Customer Portal không có icon, không có trạng thái active, sidebar trên mobile thành hàng link bị gói dòng; link hard-code `/profile/...` sẽ hỏng nếu chạy dưới virtual directory của IIS | Layout tự viết, không dùng tag helper | `TACustomerPortal.Web/Views/Shared/_Layout.cshtml` | FIXED: dùng shell chung, `asp-controller/asp-action` |
| UI-07 | LOW | Màu và nút không thống nhất (navy `#07345C` vs palette mới; nút vàng chữ trắng chỉ đạt 2.1:1) | Không có design token chung | `ta-portal.css`, `customer.css` | FIXED: token chung, kiểm tra tương phản |
| UI-08 | LOW | Không có page header chuẩn (breadcrumb, icon, mô tả, vùng nút) | Mỗi view tự dựng | Mọi view | FIXED: header dựng từ layout qua ViewData + section `PageActions` |
| UI-09 | LOW | View `Users/Index` gọi trực tiếp `PortalDb` (truy vấn SQL trong view) | `@inject PortalDb` | `Views/Users/Index.cshtml` | FIXED: dữ liệu nạp ở controller, giữ nguyên câu truy vấn |
| UI-10 | INFO | Tài khoản không phải SYS_ADMIN chỉ thấy rất ít menu | Không phải lỗi: menu khớp đúng policy của controller (`SystemAdminOnly`, `AUTH.ROLES.VIEW`, `SYSTEM.MENUS.MANAGE`) | — | Giữ nguyên, có safe test xác nhận |

## 3. Design system

- **File dùng chung:** `css/ta-design.css` và `js/ta-shell.js` có bản giống hệt từng byte ở cả hai app; `SafeUiDesignSystemTests` fail nếu hai bản lệch nhau. `customer.css` và `ta-portal.js` chỉ chứa phần riêng của từng app.
- **Token màu:** Navy `#0B2E52`, Sidebar `#082746`, Blue `#1677E8`, Gold `#E5A900`, Background `#F5F7FB`, Surface `#FFF`, Text `#17253D` / `#64748B`, Border `#E2E8F0`, Success `#16803C`, Warning `#B7791F`, Danger `#DC3545`; test kiểm tra đủ 12 giá trị.
- **Tương phản (WCAG AA):**
  - Chữ trắng trên `#1677E8` chỉ đạt 4.34:1, nên nút chính và link dùng `--ta-blue-strong #0F64C6` (5.74:1). `#1677E8` giữ cho viền focus, chỉ báo active và icon.
  - Nút vàng dùng chữ navy (6.55:1) thay vì chữ trắng (2.1:1).
  - Chữ badge dùng tông đậm: warning `#8A5A0C`, danger `#B42332`.
- **Thang đo:**
  - Spacing 4/8/12/16/24/32px.
  - Bo góc: card 12px, nút 8px.
  - Cỡ chữ: tiêu đề trang 28px (22px trên mobile), tiêu đề section 18px, body và bảng 14px, nhãn 13px.
  - Font: Inter, nếu không có thì dùng Segoe UI hoặc font hệ thống.
- **Thành phần:**
  - Bố cục: shell (sidebar, topbar, breadcrumb), page header, card.
  - Nút:
    - Primary màu xanh: Thêm, Lưu, Xác nhận.
    - Secondary nền trắng: Quay lại, Hủy, Tài khoản.
    - Soft-primary: Sửa trong bảng.
    - Vàng thương hiệu: CTA chính của trang danh sách.
    - Danger và outline-danger: Xóa, Thu hồi.
    - Icon-button, bắt buộc có `aria-label`.
  - Hiển thị dữ liệu: badge (success, warning, danger, info, neutral), chip mã `ta-code`, bảng có toolbar, footer đếm số dòng, cột Thao tác sticky và chế độ thẻ trên mobile.
  - Trạng thái và form: empty state, alert có tiêu đề và nút đóng, form grid 2 hoặc 3 cột, switch row, dropdown, modal, KPI card.
- **Icon:**
  - `TAPortal.Shared.UiIcon`: 62 icon SVG inline lấy từ Tabler Icons 3.19.0 (MIT).
  - Icon mặc định có `aria-hidden`; khi truyền label thì có `role="img"` và `aria-label` đã được encode.
  - Không cần webfont hay CDN, và không phụ thuộc đường dẫn khi chạy dưới virtual directory của IIS.
- **Thư viện:** Tabler core 1.0.0-beta21 (cùng phiên bản đang dùng) được đặt tại `wwwroot/lib/tabler/`. Integrity npm sha512 đã được kiểm tra; `.gitattributes` giữ nguyên byte; license nằm trong `THIRD-PARTY-NOTICES.md`.

## 4. Sidebar

- **Kích thước và màu:** rộng 264px, nền `#082746`, logo ở đầu.
- **Cấu trúc menu:**
  - Tổng quan.
  - Nhóm "Quản lý khách hàng": Khách hàng, Tài khoản Customer Portal.
  - Nhóm "Quản lý hệ thống": Người dùng, Vai trò & phân quyền, Quản lý menu, Cấu hình hệ thống (Sắp có), Nhật ký hoạt động (Sắp có).
- **Hiển thị:** icon và nhãn thẳng hàng; mục active có nền sáng hơn, thanh vàng 4px và icon vàng; nhãn dài hiện "…" thay vì xuống dòng.
- **Phân quyền:** điều kiện hiển thị giữ nguyên như cũ. Nhóm không có mục nào thì không hiện tiêu đề nhóm; safe test kiểm tra tài khoản `OWNER` có `AUTH.ROLES.VIEW`.
- **Desktop (≥ 1200px):** sidebar cố định, nút ☰ thu gọn thành rail 72px (ghi nhớ trong `localStorage`, có try/catch).
- **Tablet (768–1199px):** mặc định rail icon (nhãn ẩn trực quan nhưng screen reader vẫn đọc, có tooltip `title`); nút ☰ mở menu đầy đủ phủ lên nội dung.
- **Mobile (< 768px):** drawer off-canvas có overlay, nút ✕ và phím Esc; đóng drawer thì focus quay về nút ☰; menu dài cuộn bên trong vùng nav. Có skip link "Bỏ qua đến nội dung chính".

## 5. Trang Khách hàng

- **Page header:** icon, tiêu đề "Danh sách khách hàng", mô tả; nút "Tài khoản Customer Portal" (outline) và "Thêm khách hàng" (vàng).
- **Bảng:**
  - Toolbar có ô tìm kiếm kèm icon; logic lọc phía client `taFilterRows` giữ nguyên, có thêm bộ đếm "Hiển thị x / N".
  - Mã KH hiển thị dạng chip code; trạng thái là badge "Hoạt động" (xanh) hoặc "Ngừng hoạt động" (đỏ).
  - Mỗi dòng có hai nút "Sửa" và "Tài khoản".
- **Form Thêm/Sửa:** 2 cột; ô "Trạng thái" là switch; nút Quay lại và Lưu. Trường, validation và route giữ nguyên.
- **Mobile:** mỗi dòng thành một thẻ, hai nút chia đều chiều rộng.

## 6. Trang Người dùng

- **Cột "Người dùng":** gồm avatar chữ cái đầu, họ tên, tên đăng nhập và badge "Bạn" cho tài khoản đang đăng nhập.
- **Cột khác:** vai trò là badge; trạng thái là badge xanh (hoạt động), vàng (bị khóa theo trạng thái) hoặc đỏ (đã vô hiệu hóa).
- **Thao tác:** "Sửa", "Vai trò", và menu ⋮ (Bootstrap dropdown, dùng được bằng chuột và bàn phím) gồm "Khóa / mở đăng nhập" (mở form Sửa tại switch `#IsActive`) và "Xóa người dùng" (mở trang xác nhận sẵn có; với tài khoản của chính mình thì mục này bị vô hiệu).
- **Phân quyền:** vẫn kiểm tra ở server; ẩn nút không thay thế cho kiểm tra quyền.
- **Các màn hình Sửa / Vai trò / Xóa:** dùng chung header, card, ô chọn vai trò dạng thẻ và alert. Nút xác nhận xóa giữ nguyên markup mà test đang khóa.

## 7. Customer Portal

- **Shell:** dùng cùng shell và design system với Admin.
- **Sidebar:** Tổng quan; nhóm Doanh nghiệp (Hồ sơ, Chuyển doanh nghiệp); nhóm Tài khoản (Thông tin cá nhân, Đổi mật khẩu); nút Đăng xuất ở chân.
- **Topbar:** tiêu đề "Cổng thông tin khách hàng" và avatar.
- **Màn hình:**
  - Login: thẻ đăng nhập có icon và nút ẩn/hiện mật khẩu.
  - Tổng quan: banner doanh nghiệp và 3 KPI. Lưới "Dịch vụ tài chính" giữ nguyên 6 mục "Chưa kích hoạt" sẵn có, không thêm module nào.
  - Chọn doanh nghiệp: dạng thẻ.
  - Hồ sơ doanh nghiệp và Hồ sơ cá nhân: bảng định nghĩa (dl).
  - Đổi mật khẩu: dạng card.
- Không có module mới; mục "Tài liệu" trong mockup **không** được thêm vì không có route.

## 8. So với mockup

**Giống:**
- Sidebar navy có nhóm, icon, thanh vàng cho mục active, drawer mobile có nút ✕.
- Topbar có breadcrumb và avatar kèm tên + vai trò.
- Header trang có icon tiêu đề.
- Bảng có chip mã, badge trạng thái, nút Sửa (xanh nhạt) và Tài khoản (trắng); nút vàng "Thêm khách hàng"; danh sách mobile dạng thẻ.
- Form 2 cột; badge Hoạt động / Tạm khóa / Ngừng hoạt động; alert 4 loại có nút đóng.
- Customer Portal cùng ngôn ngữ thiết kế.

**Khác, và lý do:**

| Mockup | Thực hiện | Lý do |
|---|---|---|
| Chuông thông báo (badge 3) | Không có | Chưa có chức năng thông báo; quy tắc không thêm tính năng theo ảnh |
| Menu ⋮ của khách hàng: Xem chi tiết / Ngừng hoạt động / Xóa | Không có ⋮ ở bảng khách hàng | Không có route Xem/Ngừng/Xóa khách hàng; ngừng hoạt động nằm trong form Sửa |
| Phân trang "10 / trang" | Chỉ có bộ đếm "Hiển thị x / N" | Danh sách hiện chưa phân trang; không đổi truy vấn (đề xuất ở mục 11) |
| Trường "Loại khách hàng" trong form | Không có | Không có trong model/schema |
| Chữ trắng trên nút vàng | Chữ navy | Tương phản 2.1:1 không đạt WCAG |
| Nút "Lưu" màu xanh lá | Primary xanh dương | Spec mục 7 quy định Lưu dùng Primary; form mẫu trong mockup cũng dùng xanh dương |
| Customer Portal có mục "Tài liệu" | Không có | Không có route/module |
| Mục "Cấu hình hệ thống", "Nhật ký hoạt động" hoạt động | Hiển thị bị vô hiệu kèm nhãn "Sắp có" | Menu đã có từ trước nhưng chức năng chưa triển khai |

## 9. File đã sửa (source commit `70917eb`)

- **Shared:**
  - ADD `src/TAPortal.Shared/UiIcon.cs`
  - ADD `THIRD-PARTY-NOTICES.md`
  - MODIFY `.gitattributes`
- **Admin:**
  - MODIFY `Views/Shared/_Layout.cshtml`, `_ViewImports.cshtml`
  - MODIFY `Customers/{Index,Edit}`, `Users/{Index,Edit,Delete,Roles}`, `CustomerPortalAccounts/Index`, `Home/{Index,Error}`, `Roles/Index`, `Menus/Index`, `Account/{Login,Profile,ChangePassword,AccessDenied}`
  - MODIFY `Controllers/PortalControllers.cs` (chỉ thay đổi cách Users Index nạp dữ liệu)
  - RENAME `wwwroot/css/ta-portal.css` → `ta-design.css` (viết lại)
  - ADD `wwwroot/js/ta-shell.js`, `wwwroot/lib/tabler/{tabler.min.css, tabler.min.js, LICENSE}`
  - MODIFY `wwwroot/js/ta-portal.js`
- **Customer:**
  - MODIFY `Views/Shared/_Layout.cshtml`, `_ViewImports.cshtml`
  - MODIFY `Account/{Login,SelectCustomer,ChangePassword,AccessDenied,Error}`, `Dashboard/Index`, `Profile/{Company,Personal}`
  - MODIFY `wwwroot/css/customer.css`
  - ADD `wwwroot/css/ta-design.css`, `wwwroot/js/ta-shell.js`, `wwwroot/lib/tabler/{tabler.min.css, LICENSE}`
- **Test:**
  - ADD `SafeUiDesignSystemTests.cs` (20 test), `VisualSnapshotTests.cs`
  - MODIFY `SafeAdminDashboardUxTests.cs` (markup sidebar mới), `SafeAdminSecurityTests.cs`, `SafeReleaseRegressionTests.cs` (đường dẫn `/css/ta-design.css`)
- **Không đổi:**
  - Không đổi route, controller action, authorization policy, model hay validation.
  - Không đổi `SecurityHeaders`, cookie, Data Protection, `appsettings`, SQL hay schema.

## 10. Build và test

- `dotnet restore` OK; `dotnet build TAPortal.sln -c Release` 0 cảnh báo / 0 lỗi (gồm biên dịch Razor của cả hai app).
- **Safe tests: 235 PASS / 0 FAIL / 0 SKIP** (215 cũ và 20 mới). Operator không bắt buộc bước này; nó được chạy làm bằng chứng không thay đổi route và nghiệp vụ.
- Codec: unittest OK, validate PASS; `git diff --check` OK.

## 11. Tồn tại và đề xuất

- **VISUAL_NOT_VERIFIED** trên trình duyệt thật, dữ liệu thật và IIS: operator kiểm tra theo Phase C của runbook.
- **Phân trang phía server** cho Khách hàng và Người dùng: cần quyết định riêng vì sẽ đổi truy vấn.
- **CSP:** đã hết phụ thuộc CDN, nên có thể siết CSP `script-src` / `style-src 'self'`. Việc này còn cần bỏ các handler inline (`oninput`, `onclick`, `onchange`) và là thay đổi bảo mật riêng: **PENDING_APPROVAL**.
- **Bản sao file chung:** `ta-design.css` và `ta-shell.js` đang được chép tay giữa hai app (có test bảo vệ). Phương án gọn hơn là chuyển Shared sang Razor Class Library với static web assets; đây là thay đổi kiến trúc nên cần phê duyệt.
