# Feature: TINGEE-INTEGRATION

- Feature ID: `TINGEE-INTEGRATION`
- Tên: Tingee payments, bank linking, orders/QR, Zalo OA messaging and e-invoicing for Customer Portal tenants
- Mục tiêu: T.A (Master Merchant) cấp tài khoản Tingee cho từng customer; customer tự liên kết ngân hàng, quản lý khách hàng của họ, tạo đơn hàng có QR thanh toán (có hạn), nhận webhook gạch nợ, đối soát cuối ngày, gửi tin Zalo (ZNS + tư vấn OA) và tự xuất hóa đơn điện tử.
- Phạm vi / ngoài phạm vi: Trong phạm vi: Admin (cấu hình, liên kết customer↔merchant), Customer Portal (ngân hàng, khách hàng, đơn hàng, Zalo, hóa đơn, đối soát), TAPortal.Webhooks (nhận webhook + worker), TAPortal.Integrations (client Tingee/Zalo). Ngoài phạm vi: chi hộ (direct-link), TingeeBox, POS, direct debit.
- Kiến trúc / bảng liên quan: `src/TAPortal.Integrations` (thư viện), `src/TAPortal.Webhooks` (app nhận webhook + worker nền, hàng đợi SQL `UPDLOCK, READPAST`). Bảng mới sẽ được soạn bằng migration do operator chạy.
- Các phụ thuộc: Tingee Master Merchant + key UAT (T.A sẽ cung cấp — PENDING_OPERATOR); URL HTTPS công khai cho webhook; Zalo App của từng customer; phân quyền linh hoạt Customer Portal (CUSTOMER-PORTAL-RBAC, làm trước).
- Trạng thái: ACTIVE

## Quyết định đã chốt với operator (2026-10-30)
1. Webhook + worker ở app riêng `TAPortal.Webhooks`; thư viện dùng chung `TAPortal.Integrations` (không gộp vào TAPortal.Shared).
2. Customer có trang cấu hình ngân hàng riêng (form theo từng ngân hàng qua create-va/confirm-va, cấu hình TK doanh nghiệp), không dùng Bank Link SDK.
3. Nhận biến động theo STK gốc (`isNotifyAccountNumber=true`) ở ngân hàng hỗ trợ; ngân hàng khác chỉ qua VA.
4. Zalo: cả ZNS và tin tư vấn OA; customer dùng Zalo App/OA của chính họ và tự chịu phí.
5. Phương án B: một app tích hợp gồm nhận webhook + hosted worker, thiết kế để tách worker thành Windows Service sau này.
6. Đơn hàng: QR `dynamic-recurring-payment`, cho thanh toán nhiều lần đến khi đủ; chỉ khi đủ mới khóa QR và xử lý hóa đơn.
7. Tự xuất hóa đơn khi đơn thanh toán đủ: mặc định BẬT ở cấp customer, chỉnh được từng đơn.
8. Phân quyền linh hoạt: vai trò do customer tự định nghĩa, quyền theo chức năng; mọi người có quyền xem đơn đều thấy toàn bộ đơn của doanh nghiệp.
9. Thứ tự: P1a phân quyền → P1b nền tảng Tingee → P2 ngân hàng → P3 đơn/QR/webhook → P4 QR tĩnh + đối soát → P5 Zalo → P6 hóa đơn.

## Change history

| Change ID | Ngày UTC | Nội dung | Commit SHA | Migration | Publish | Merge |
|---|---|---|---|---|---|---|
| `TINGEE-INTEGRATION-CHG-20261029T200714Z-B90483` | 2026-10-29 | Scaffold TAPortal.Integrations and TAPortal.Webhooks projects | f7979e10a7ce69174332db638c5ef1fec95369b4 | NONE_REQUIRED | NOT_RUN (no configured target) | PENDING_APPROVAL |
| `TINGEE-INTEGRATION-CHG-20261029T203104Z-1FE09C` | 2026-10-29 | Tingee merchant mapping (Admin); bank accounts, contacts and QR orders (Customer); webhook inbox | 91e36178531a9d8b525f857b68f36dc88798371e | NOT_EXECUTED (011) | PUBLISHED (local ×3) / NOT_DEPLOYED | PENDING_APPROVAL |

## Current status (2026-10-30)
Admin: tab Thanh toán Tingee. Customer: ngân hàng, khách hàng, đơn hàng QR, cài đặt thanh toán. Webhooks: inbox SQL. Migration 011 NOT_EXECUTED; Tingee chưa có key (NOT_VERIFIED). Tiếp theo: worker xử lý inbox + đối soát, phân quyền linh hoạt, Zalo, hóa đơn.
| `TINGEE-INTEGRATION-CHG-20261030T103236Z-05E4FE` | 2026-10-30 | Direct Merchant mode for testing without Master Merchant; text diagnostic script | 53dbe68cf9907ff3931fd593203435c15a180029 | OPERATOR_REPORTED_011_OK; 014 NOT_EXECUTED | PUBLISHED (local ×3) / NOT_DEPLOYED | PENDING_APPROVAL |

## Current status (2026-10-30, sau change 05E4FE)
Có chế độ `Tingee__Mode=Direct` để test bằng tài khoản Merchant thường; Master Merchant vẫn PENDING_OPERATOR. Operator báo migration 011 OK, nhưng `dbo.CustomerContacts` có từ trước và chưa được đối chiếu (chờ script 014). Tingee thật NOT_VERIFIED. Publish tại `src\publish\deployed-<PROJECT_NAME>`.
| `TINGEE-INTEGRATION-CHG-20261030T124900Z-448FB4` | 2026-10-30 | Fix collation conflict in diagnostic script 014 | a04dab999b24c3779607a0296eb946ff4e5d0deb | NONE_REQUIRED | NOT_RUN | PENDING_APPROVAL |
| `TINGEE-INTEGRATION-CHG-20261030T131324Z-F23ADA` | 2026-10-30 | Separate CustomerBuyers table; stop using the existing CustomerContacts table | 5db5161254907991e697eca76e3b46133edb5e33 | OPERATOR_CONFIRMED (012) | PUBLISHED (local: Customer) / NOT_DEPLOYED | PENDING_APPROVAL |

## Current status (2026-10-30, sau change F23ADA)
Khách hàng của doanh nghiệp lưu ở `dbo.CustomerBuyers`; `dbo.CustomerContacts` có sẵn không được dùng. Migration 012 NOT_EXECUTED: trang khách hàng và đơn hàng chưa dùng được cho đến khi operator chạy.

## Current status (2026-10-30, operator xác nhận migration 012)
Database sẵn sàng cho phần thanh toán (verify PASS). Tiếp theo: operator cấu hình `Tingee__Mode=Direct` và test ngân hàng, đơn QR. Tingee thật NOT_VERIFIED.
