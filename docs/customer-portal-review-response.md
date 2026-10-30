# PR #7 — phản hồi review và nghiệm thu còn lại

Review đã đọc: [review 5657802957](https://github.com/nguyenduc216/TAPortal/pull/7#pullrequestreview-5657802957), gồm 6 mục. Không có inline review thread ở thời điểm bắt đầu. Giữ PR draft, không merge, không deploy, không chạy SQL trên production.

## 1. P1 — phạm vi Customer/Agent: đã sửa bằng chế độ SYS_ADMIN-only

Chọn rõ phương án Super Admin-only được reviewer cho phép. Repository chưa có mapping Agent–Customer được xác minh, vì vậy không tự tạo/đoán quyền Agent. Chỉ active SYS_ADMIN được quản lý membership và xem overview/danh sách Customers/Users toàn hệ thống. Customer OWNER/ADMIN/VIEWER không phải vai trò hệ thống; Agent/nhân viên có direct permission hoặc role permission vẫn bị từ chối.

`TAPortal.Web/Program.cs` đăng ký SystemAdminOnly; CustomerPortalAccountsController, HomeController, CustomersController và UsersController yêu cầu policy này. `MembershipAdminStore.List/Save/SetActive` kiểm tra lại Users/UserRoles/Roles từ DB; Save và SetActive kiểm tra trong cùng serializable transaction với thao tác ghi. Actor lấy từ principal, không lấy từ form/query. Không chỉ lọc dropdown.

Bằng chứng: RestrictedActorsCannotListOrMutateAnyCustomer (customer/direct-permission/agent-role); StoreRechecksLiveAdministratorStatus (6 trạng thái user/role); PermissionAloneCannotOpenGlobalListsOrInvokeManagementActions; AdminRoleRevocationAppliesToExistingSession. Tests kiểm tra cả CustomerId được gán và CustomerId khác, không ghi dữ liệu khi bị từ chối. Đây là kiểm chứng từ chối delegated Agent management, không phải tuyên bố đã có full Agent scope.

## 2. P1 — staging schema: cần người vận hành xác minh

Không có Database__ConnectionString hay cấu hình staging được chỉ định trong phiên làm việc. Không có SQL connector staging. Chưa xác nhận server/database triển khai là TAPortal hay TAPortalSys và chưa audit schema thực tế. Chỉ dùng SQL LocalDB ngẫu nhiên của tests, không thử đoán hoặc kết nối production.

DBA cần chọn staging database rõ ràng, chạy `database/scripts/003-customer-portal-schema-audit.sql` chỉ đọc; xác nhận DB_NAME(), schema Users/Customers, GUID keys, trạng thái/soft-delete, mapping tương đương và Agent relation nếu có. Review migration 008, backup staging và SQL identity/grants theo script 006; sau review mới tự chạy migration trên staging, rồi script 004 verify. Chưa có thay đổi migration hoặc schema production trong đợt sửa review.

## 3. P1 — UAT IIS: chưa đủ bằng chứng nghiệm thu

W3SVC/WAS đang chạy trên máy, nhưng `appcmd list site` bị từ chối đọc redirection.config do insufficient permissions. Không thay đổi cấu hình IIS/ACL hoặc tạo site trên máy. Chưa có staging URL/certificate/application pool identity để UAT. Build, HTTP in-process tests và publish không được coi là UAT IIS/browser.

Người vận hành cần thực hiện trên staging:

1. Deploy hai publish outputs vào hai site/application pools riêng, cấu hình HTTPS và AllowedHosts.
2. Cấu hình connection strings bằng secret store, key folders riêng ngoài web root, đúng identity/ACL và DPAPI; không dùng chung key ring/cookie. Tạo phiên rồi recycle app pool, xác nhận phiên còn hợp lệ; xác nhận cookie customer không dùng được trên admin.
3. Dùng SYS_ADMIN tạo/gán user A–Customer A và user B–Customer B, thử đổi URL/CustomerId, chuyển doanh nghiệp, thu hồi/kích hoạt membership và đổi mật khẩu. Kiểm tra phiên cũ mất hiệu lực ở cả hai site.
4. Với Agent/nhân viên chỉ có CUSTOMERS.PORTAL.MANAGE hoặc AUTH.USERS.VIEW, kiểm tra trực tiếp GET /, /Customers, /Users, /CustomerPortalAccounts và POST Save/SetActive đều bị từ chối; không cấp SYS_ADMIN để vượt hạn chế. Việc thu hẹp quyền của nhân viên cũ là thay đổi có chủ ý cần xác nhận khi UAT.
5. Kiểm tra hồi quy SYS_ADMIN: overview, Customers, Users/create, Roles, Menus, profile, đổi mật khẩu, logout. Kiểm tra desktop/tablet/mobile và tải CDN Tabler trong mạng triển khai.
6. Lưu ảnh chụp, timestamps, site bindings/identity (không ghi secret), kết quả key persistence, revocation và logs riêng vào bằng chứng nghiệm thu.

## 4. P2 — overview/danh sách toàn hệ thống: đã sửa

Không coi CUSTOMERS.VIEW, CUSTOMERS.PORTAL.MANAGE hoặc AUTH.USERS.VIEW là phạm vi tenant. Global pages chỉ SYS_ADMIN; menu ẩn theo cùng điều kiện để tránh liên kết không dùng được, nhưng backend vẫn chặn URL/action. Nhân viên không phải SYS_ADMIN đăng nhập được hệ thống theo quyền hiện có sẽ về hồ sơ cá nhân thay vì global dashboard. Các permission rows cũ được giữ, không chỉnh grants/database production.

Bằng chứng HTTP: PermissionAloneCannotOpenGlobalListsOrInvokeManagementActions dùng token CSRF hợp lệ cho POST, kiểm tra không tạo user/membership ngoài quyền. Positive test AdminCanProvisionWithoutGrantingSystemRole và tests Customer Portal isolation hiện có được giữ.

## 5. P2 — throttle/proxy: đã sửa trong app; WAF thực tế cần UAT

Giữ 10 POST login/phút/IP/process; thêm 5 lần thử/phút/tài khoản chuẩn hóa, khóa partition bằng SHA-256, áp dụng giống nhau cho account có/không tồn tại. Không log username hoặc credential trong bộ đếm. Đổi IP không reset account throttle. Đây vẫn là bộ đếm trong mỗi process, không tuyên bố đã có distributed rate limiting.

Mặc định không xử lý forwarded headers. Cấu hình chính xác `ReverseProxy:KnownProxies` chỉ cho socket IP proxy tin cậy, một hop; middleware chạy trước HTTPS/rate limiter. Header từ nguồn không tin cậy bị bỏ qua. Runbook có cấu hình và yêu cầu proxy ghi đè header, chặn direct backend access, WAF shared counters khi multi-instance.

Bằng chứng: LoginRateLimited (header giả không bypass IP limit); AccountThrottleNormalizesNamesAndDoesNotRevealExistence; ForwardedHeadersOnlyAffectLimitsForConfiguredSocketProxy (trusted/untrusted); ChangingForwardedIpCannotBypassAccountThrottle; UntrustedForwardedProtoCannotDowngradeRequest. Chưa kiểm tra gateway/WAF/multi-instance thực tế; người vận hành phải thử qua topology staging thật.

## 6. P2 — GitHub Actions

Baseline bb3c648 đã xác minh cả workflow build và Customer Portal MVP 01 success. Sau push sửa review, kiểm tra lại đúng head commit và công bố links run/artifact trong phản hồi PR #7 (Conversation). Workflow Windows upload TRX `customer-portal-test-results`; build Linux được giữ. Không dùng baseline success để thay cho kết quả head mới.

## Kết quả local của đợt sửa review

Release solution build: 0 lỗi, 0 cảnh báo. 42 tests pass, 0 fail, 0 skipped, gồm 24 test cases ban đầu và 18 case mới. SQL LocalDB thật, database test riêng tự tạo/xóa; HTTP in-process ở cả hai apps và test trusted/untrusted socket. Không có test dùng production. Publish riêng hai websites được kiểm tra lại. TRX local: publish/test-results/customer-portal-review.trx (không commit).

Các mục code/security đã xử lý: 1, 4 và phần ứng dụng của 5. Các gate chưa đủ để Ready for review/merge: audit schema staging (2), UAT IIS/browser (3), WAF topology thực tế (phần 5). CI head được ghi nhận bằng link run/artifact trên PR sau push (6).
