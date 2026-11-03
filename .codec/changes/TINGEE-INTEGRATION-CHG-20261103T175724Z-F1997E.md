# Change TINGEE-INTEGRATION-CHG-20261103T175724Z-F1997E

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261103T175724Z-F1997E`
- Created UTC: 2026-11-03T17:57:24.882172+00:00
- Tiêu đề: Send non-ASCII JSON unescaped to Tingee (Invalid signature 97)
- Trạng thái: DEPLOYED (portal, invoice)

## Yêu cầu và phạm vi
"Tạo tài khoản Tingee qua API" cho TVGPTA (tên "Tư vấn giải pháp T.A") bị Tingee trả 97 "Invalid signature". Kiểm chứng trên server bằng lệnh chỉ đọc merchant/get-paging (key đã cấu hình): body ASCII → 00; filter "Tư" (escape) → 97; filter "Tư" UTF-8 thô → 00. Nguyên nhân: System.Text.Json mặc định escape ký tự ngoài ASCII; Tingee tính chữ ký trên body đã giải mã. Sửa: TingeeClient dùng JavaScriptEncoder.UnsafeRelaxedJsonEscaping (body chỉ gửi tới API Tingee, không render HTML).

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Integrations/Tingee/TingeeClient.cs`.
- MODIFY `tests/TACustomerPortal.Tests/SafeTingeeDirectMerchantTests.cs` (+1 test).

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path:
- Thứ tự chạy:
- Preflight / Verify / Rollback:
- Execution: NOT_APPLICABLE

## Test và kết quả
- Build Release 0 lỗi; Safe tests 324/324.

## Publish
- portal: DEPLOYED 2026-11-03 UTC, source 116896f, artifact 874843a889892939b2dc0fb7ab6867b64de410c92b004b60fd3a499c9c7d4e10, package E142767C52641A74B6527392ED60E80FCC5EBF75FA97D1191A659435CAFF9ABA, health 200.
- invoice: DEPLOYED 2026-11-03 UTC, source 116896f, artifact a94d189b35ce2f254bd5c34c0e6cc8f22df2042b117290c018468330460e87ef, package FCB8B0280745B0328DFF7ADE76CBCB5545B622A7050200397C6351947639B02F, health 200.

## Git
- Branch:
- Commit SHA:
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Ảnh hưởng cả Customer Portal (tên khách hàng/nội dung có dấu trong create-va, QR purpose): trước đây cũng sẽ lỗi 97, nay hết.
