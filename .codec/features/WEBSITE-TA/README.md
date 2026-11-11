# Feature: WEBSITE-TA

- Feature ID: `WEBSITE-TA`
- Tên: Website TA (tuvangiaiphapta.vn) — CMS nằm trong TA Portal + dự án frontend `TAWebsite.Web`
- Trạng thái: ACTIVE

## Mục tiêu

Thay landing page tĩnh hiện tại (IIS site `TA.LandingPage`, `H:\WEBDEPLOYED\T.A\LandingPage\index.html`, nội dung "Lập trình phần mềm & Chuyển đổi số", không backend) bằng website dịch vụ thành lập doanh nghiệp – kế toán – thuế của T.A, dựng từ bộ mockup `websiteTA/` (Homepage V10, Giới thiệu V9, Dịch vụ / Liên hệ / Tin tức / Bài viết V1). Toàn bộ nội dung (chữ, hình ảnh, nút, thứ tự section, SEO, thông tin liên hệ, bài viết) được cập nhật trong mục **Website TA** của TA Portal, không phải sửa code.

## Kiến trúc

```
TA Portal (TAPortal.Web, portal.tuvangiaiphapta.vn)       Website (TAWebsite.Web, tuvangiaiphapta.vn)
  Mục "Website TA"                                          Razor SSR, đọc nội dung chỉ-đọc
   ├ Trang & section  ──ghi──►  TAPortalSys (dbo.Website*) ◄──đọc── trang chủ, giới thiệu, dịch vụ…
   ├ Bài viết / chuyên mục                                         /tin-tuc, /tin-tuc/{slug}
   ├ Thư viện ảnh (DB)                                             /media/{id} (cache, ETag)
   ├ Cài đặt chung                                                 header/footer/hotline/bản đồ
   └ Liên hệ – báo giá  ◄──────────────ghi lead──────────────────  form /lien-he
                 src/TAWebsite.Core (dùng chung: định nghĩa trang/section, schema trường, SQL đọc)
```

- **Cấu trúc trang nằm trong code, nội dung nằm trong DB.** `TAWebsite.Core/SiteMap` khai báo từng trang (khóa, đường dẫn, tên) và danh sách section; mỗi section có *loại* (hero, thẻ, quy trình, FAQ, CTA…) với danh sách trường (chữ, đoạn văn, ảnh, link, danh sách lặp). Nội dung mặc định lấy từ mockup. DB chỉ lưu phần đã sửa → không cần seed; thêm section mới chỉ cần deploy code.
- Portal sinh form sửa tự động theo schema trường; website render từng loại section bằng partial Razor.
- Ảnh lưu trong DB (`varbinary(max)`, giống logo/sản phẩm hiện có), website phục vụ qua `/media/{id}` có cache. Không phụ thuộc thư mục chia sẻ giữa 2 site IIS.
- Mọi ghi dữ liệu đi qua Portal (transaction, kiểm tra quyền trong SQL, audit, RowVersion). Website chỉ đọc, trừ ghi lead liên hệ.

## Bảng (migration 035, dbo, SQL Server 2014)

| Bảng | Nội dung |
|---|---|
| `WebsitePages` | khóa trang, SEO title/description |
| `WebsiteSections` | (PageKey, SectionKey) → ContentJson, thứ tự, ẩn/hiện |
| `WebsiteMedia` | ảnh: tên, loại, kích thước, alt, dữ liệu, SHA256 |
| `WebsitePostCategories` | chuyên mục bài viết |
| `WebsitePosts` | bài viết: slug, tiêu đề, tóm tắt, nội dung, ảnh bìa, chuyên mục, tác giả, tag, nháp/xuất bản/hẹn giờ, nổi bật, SEO, lượt xem |
| `WebsiteSettings` | khóa–giá trị: hotline, email, Zalo, địa chỉ, bản đồ, mạng xã hội, giờ làm việc |
| `WebsiteLeads` | yêu cầu liên hệ – báo giá từ website, trạng thái xử lý, ghi chú |

Quyền mới (module `WEBSITE`): `WEBSITE.VIEW`, `WEBSITE.CONTENT.EDIT`, `WEBSITE.POSTS.EDIT`, `WEBSITE.MEDIA.EDIT`, `WEBSITE.SETTINGS.EDIT`, `WEBSITE.LEADS.VIEW`, `WEBSITE.LEADS.EDIT` — cấp cho SYS_ADMIN.

## Lộ trình

| GĐ | Nội dung | Kết quả |
|---|---|---|
| 1 | Nền tảng: migration 035, `TAWebsite.Core`, mục Website TA trong Portal (trang/section, ảnh, cài đặt, bài viết, liên hệ), dự án `TAWebsite.Web` với layout + Trang chủ + Liên hệ + Tin tức + Bài viết | Sửa nội dung trong Portal, xem trên website chạy local |
| 2 | Chuyển nốt các trang mockup: Giới thiệu, Dịch vụ, 2 trang dịch vụ chi tiết; trình soạn bài viết (định dạng, chèn ảnh), xem trước bản nháp | Đủ trang như mockup |
| 3 | Lead: email thông báo cho T.A khi có yêu cầu mới, chống spam nâng cao, xuất Excel | Form báo giá dùng thật |
| 4 | SEO & hiệu năng: sitemap.xml, robots, Open Graph, schema.org (LocalBusiness, Article), redirect URL cũ, thu nhỏ ảnh | Sẵn sàng index |
| 5 | Go-live: target deploy `website`, chuyển site IIS `TA.LandingPage` sang ASP.NET Core (**operator**: app pool No Managed Code, appsettings, đăng ký target trong `deploy-config.json`), giữ bản tĩnh cũ làm backup | tuvangiaiphapta.vn chạy bản mới |
| 6 | Vận hành: lịch đăng bài, thống kê lượt xem, phân quyền biên tập viên, lịch sử phiên bản nội dung | |

## Phụ thuộc / quyết định chờ operator
- Go-live cần operator đổi cấu hình IIS `TA.LandingPage` và đăng ký target `website` (AGENTS.md cấm agent tự sửa IIS/appsettings production).
- Nội dung pháp lý trong mockup cần chuyên môn T.A rà soát trước khi xuất bản.

## Change history

| Change ID | Ngày UTC | Nội dung | Commit SHA | Migration | Publish | Merge |
|---|---|---|---|---|---|---|
| `WEBSITE-TA-CHG-20261111T163408Z-D94863` | 2026-11-11 | Giai đoạn 1 – nền tảng CMS + frontend | d0cc5c5 | EXECUTED | NOT_PUBLISHED | PENDING |
