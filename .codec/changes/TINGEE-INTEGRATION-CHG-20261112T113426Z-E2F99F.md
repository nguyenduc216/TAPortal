# Change TINGEE-INTEGRATION-CHG-20261112T113426Z-E2F99F

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261112T113426Z-E2F99F`
- Created UTC: 2026-11-12T11:34:26.894359+00:00
- Tiêu đề: Trang thanh toán: tài khoản thụ hưởng thật khi QR dùng mã VQR (MB)
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-12): trang thanh toán đơn DH261110-0001 (MB Bank) hiện "Số tài khoản VQRQANNLO4384"; không phải số tài khoản MB của T.A, chuyển khoản thì ngân hàng báo không tìm thấy. Yêu cầu hiện tài khoản thụ hưởng.

Nguyên nhân: với MB qua Tingee, cả VA (VQRQ…1102) và tài khoản của QR động (VQRQ…) là mã chữ chỉ dùng được khi quét QR; gõ tay hoặc mở ứng dụng ngân hàng điền sẵn bằng mã này đều bị từ chối.

Đã làm (`PayController.Vm`, `PayPageVm.QrOnly/AccountName`, `Views/Pay/Index.cshtml`): khi mã nhận tiền không phải dãy số và có số tài khoản gốc, trang thanh toán hiện "Tài khoản thụ hưởng" (tên chủ tài khoản) và số tài khoản gốc (nút sao chép, ảnh QR tải về cũng dùng số này), thêm ghi chú "Quét mã QR để được xác nhận tự động; chuyển khoản tay vào số tài khoản vẫn nhận được tiền nhưng người bán xác nhận sau", và bỏ nhóm ứng dụng điền sẵn (sẽ điền mã VQR). Ngân hàng khác giữ nguyên.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TACustomerPortal.Web/{Controllers/PayController.cs,Models/PaymentModels.cs,Views/Pay/Index.cshtml}`, `tests/TACustomerPortal.Tests/SafeCustomerPayPageTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Không có.

## Test và kết quả
- Release build 0 lỗi; Safe tests 668/668 (mới: AnAliasQrShowsTheRealBeneficiaryAccountAndNoPrefilledTransfer).

## Publish
- invoice. Xem report.

## Git
- Branch: feature/tx-sync
- Commit SHA: xem report
- PR:
- Merge: xem report
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Chuyển khoản tay vào số tài khoản gốc MB không có IPN (MB qua Tingee chỉ báo VA), nên cần xác nhận thủ công.
