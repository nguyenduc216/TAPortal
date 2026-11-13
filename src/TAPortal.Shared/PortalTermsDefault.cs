namespace TAPortal.Shared;

/// <summary>
/// Built-in terms of service of Hóa đơn tự động (drafted 2026-11-12 from Vietnamese law in force in November 2026; to be
/// reviewed by a lawyer). Shown until staff save their own text in the Admin portal. Bracketed fields such as [TÊN CÔNG TY]
/// are for the operator to fill in. Format: "## " starts an article, "- " a list item, a blank line a new paragraph.
/// </summary>
public static class PortalTermsDefault
{
    public static readonly DateTime Date = new(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);

    public const string Text = """
## Điều 1. Phạm vi áp dụng và chấp nhận điều khoản

Điều khoản này điều chỉnh việc sử dụng nền tảng Hóa đơn tự động tại các địa chỉ hoadon.tuvangiaiphapta.vn và hoadontudong.vn, bao gồm mọi tính năng, ứng dụng và kênh hỗ trợ đi kèm.

Khi đăng ký tài khoản, đánh dấu đồng ý trên giao diện hoặc tiếp tục sử dụng dịch vụ, khách hàng xác nhận đã đọc, hiểu và chấp nhận toàn bộ điều khoản này. Việc chấp nhận bằng phương tiện điện tử có giá trị pháp lý theo Luật Giao dịch điện tử số 20/2023/QH15 (đã được sửa đổi, bổ sung).

Nếu không đồng ý với bất kỳ nội dung nào, khách hàng vui lòng không đăng ký hoặc ngừng sử dụng dịch vụ.

## Điều 2. Giải thích từ ngữ

- Hóa đơn tự động hoặc Nhà cung cấp: đơn vị cung cấp và vận hành nền tảng, thông tin chi tiết tại Điều 16.
- Khách hàng: doanh nghiệp, hộ kinh doanh hoặc cá nhân đã đăng ký tài khoản trên nền tảng.
- Người mua: người chuyển tiền cho khách hàng theo đơn hàng, link thanh toán hoặc mã QR chuyển khoản do khách hàng tạo.
- Gói dịch vụ: tập hợp tính năng, hạn mức và thời hạn sử dụng mà khách hàng mua.
- Đối tác thanh toán: tổ chức được phép cung cấp dịch vụ trung gian thanh toán, giúp nền tảng nhận thông báo biến động số dư từ tài khoản ngân hàng của khách hàng.
- Nhà cung cấp dịch vụ hóa đơn điện tử: tổ chức cung cấp dịch vụ hóa đơn điện tử có kết nối với cơ quan thuế, được nền tảng kết nối để lập và gửi hóa đơn cho khách hàng.
- Hóa đơn điện tử: hóa đơn được lập bằng phương tiện điện tử theo pháp luật về quản lý thuế và hóa đơn, chứng từ.

## Điều 3. Đăng ký và bảo mật tài khoản

Khách hàng tự đăng ký tài khoản trên nền tảng và phải cung cấp thông tin trung thực, chính xác, đầy đủ, gồm tên, số điện thoại, email, mã số thuế (nếu có) và thông tin người đại diện. Khách hàng có trách nhiệm cập nhật khi thông tin thay đổi.

Sau khi đăng ký, mật khẩu đăng nhập được gửi đến email khách hàng đã khai báo. Khách hàng phải đổi mật khẩu này ở lần đăng nhập đầu tiên.

Khách hàng có trách nhiệm:

- Giữ bí mật mật khẩu, mã xác thực và không chia sẻ tài khoản cho người không có thẩm quyền.
- Chịu trách nhiệm về mọi thao tác thực hiện bằng tài khoản của mình, kể cả thao tác của nhân viên được khách hàng cấp quyền.
- Thông báo ngay cho Hóa đơn tự động khi phát hiện tài khoản bị truy cập trái phép.

Hóa đơn tự động không bao giờ yêu cầu khách hàng cung cấp mật khẩu qua điện thoại, tin nhắn hay email.

## Điều 4. Loại khách hàng

- Doanh nghiệp và tổ chức có mã số thuế: được sử dụng đầy đủ tính năng thu tiền, quản lý và tự động xuất hóa đơn điện tử, sau khi hoàn tất kết nối với nhà cung cấp dịch vụ hóa đơn điện tử.
- Hộ kinh doanh, cá nhân kinh doanh: được sử dụng đầy đủ tính năng như trên nếu đã đăng ký thuế và đăng ký sử dụng hóa đơn điện tử theo quy định. Theo khoản 1 Điều 11 Luật Quản lý thuế số 108/2025/QH15, mã số thuế của cá nhân, hộ gia đình, hộ kinh doanh, cá nhân kinh doanh là số định danh cá nhân được cấp theo pháp luật về căn cước.
- Cá nhân chưa đăng ký thuế hoặc chưa đăng ký sử dụng hóa đơn điện tử: chỉ được sử dụng tính năng thu tiền và quản lý giao dịch, không xuất được hóa đơn điện tử đứng tên mình trên nền tảng.

Khách hàng tự chịu trách nhiệm xác định mình thuộc loại nào và có nghĩa vụ sử dụng hóa đơn điện tử hay không theo pháp luật thuế. Khi đủ điều kiện, khách hàng có thể đề nghị nâng cấp để dùng tính năng hóa đơn.

## Điều 5. Gói dịch vụ, phí và thanh toán

Danh sách gói dịch vụ, tính năng, hạn mức và giá được công bố trên nền tảng tại thời điểm khách hàng mua. Giá đã hoặc chưa gồm thuế giá trị gia tăng được ghi rõ trên trang mua gói.

Khách hàng thanh toán phí bằng chuyển khoản theo thông tin hiển thị khi đặt mua. Gói dịch vụ được kích hoạt sau khi Hóa đơn tự động ghi nhận đủ số tiền. Hóa đơn tự động xuất hóa đơn điện tử cho phí dịch vụ theo thông tin khách hàng cung cấp.

Khi gói hết hạn hoặc hết hạn mức, một số tính năng có thể tạm dừng cho đến khi khách hàng gia hạn hoặc mua thêm. Dữ liệu của khách hàng vẫn được lưu giữ theo Điều 12.

Hóa đơn tự động có thể điều chỉnh giá gói. Giá mới áp dụng cho lần mua hoặc gia hạn tiếp theo, không áp dụng cho gói khách hàng đã thanh toán.

## Điều 6. Hoàn tiền

Phí đã thanh toán không được hoàn lại, trừ các trường hợp sau:

- Khách hàng chuyển nhầm hoặc chuyển thừa tiền.
- Gói dịch vụ không được kích hoạt hoặc không sử dụng được do lỗi của Hóa đơn tự động và không khắc phục được trong thời hạn hợp lý.
- Hóa đơn tự động chấm dứt dịch vụ mà không do lỗi của khách hàng.

Khách hàng gửi yêu cầu hoàn tiền qua [EMAIL HỖ TRỢ] kèm chứng từ chuyển khoản. Số tiền hoàn được tính tương ứng với phần thời hạn chưa sử dụng (nếu có) và chuyển về tài khoản đã thanh toán trong vòng [SỐ NGÀY HOÀN TIỀN] ngày làm việc kể từ khi hai bên xác nhận.

## Điều 7. Dịch vụ thu tiền qua chuyển khoản và mã QR

Khách hàng có thể tạo đơn hàng, gửi link thanh toán hoặc mã QR chuyển khoản cho người mua. Người mua chuyển khoản trực tiếp vào tài khoản ngân hàng của khách hàng.

Hóa đơn tự động không nhận, không giữ và không chi hộ tiền của khách hàng hay người mua. Hóa đơn tự động không phải là ngân hàng, ví điện tử hay tổ chức cung ứng dịch vụ trung gian thanh toán.

Để nhận thông báo biến động số dư, khách hàng liên kết tài khoản ngân hàng thông qua đối tác thanh toán. Việc liên kết chỉ cho phép nhận thông tin giao dịch đến; nền tảng không có quyền rút, chuyển hay phong tỏa tiền trong tài khoản. Việc liên kết tuân theo điều kiện của ngân hàng và đối tác thanh toán, phù hợp Nghị định số 52/2024/NĐ-CP về thanh toán không dùng tiền mặt.

Khách hàng có trách nhiệm:

- Đối chiếu giao dịch với sao kê ngân hàng. Sao kê của ngân hàng là căn cứ cuối cùng.
- Tự giải quyết với người mua và ngân hàng về giao dịch nhầm, thiếu, thừa, hoàn tiền hoặc tra soát.
- Không sử dụng tính năng thu tiền cho giao dịch trái pháp luật.

Thông báo biến động số dư có thể chậm hoặc gián đoạn do ngân hàng, đối tác thanh toán hoặc mạng viễn thông. Khách hàng có thể hủy liên kết tài khoản bất kỳ lúc nào.

## Điều 8. Hóa đơn điện tử

Khi khách hàng bật tính năng tự động, nền tảng gửi dữ liệu đơn hàng đã nhận tiền đến nhà cung cấp dịch vụ hóa đơn điện tử để lập hóa đơn theo mẫu và cấu hình khách hàng đã thiết lập. Khách hàng cũng có thể lập, điều chỉnh, thay thế hóa đơn thủ công theo quy định.

Khách hàng là người bán và là người lập hóa đơn. Khách hàng chịu trách nhiệm về:

- Tính chính xác của thông tin người bán, người mua, hàng hóa, dịch vụ, giá, thuế suất và thời điểm lập hóa đơn.
- Việc lựa chọn bật hay tắt chế độ tự động, phù hợp với thời điểm lập hóa đơn mà pháp luật quy định cho từng loại giao dịch, ví dụ trường hợp nhận tiền đặt cọc hoặc ứng trước.
- Nghĩa vụ kê khai, nộp thuế và xử lý hóa đơn có sai sót theo Luật Quản lý thuế số 108/2025/QH15, Nghị định số 254/2026/NĐ-CP về hóa đơn điện tử, chứng từ điện tử và văn bản hướng dẫn.

Hóa đơn tự động là công cụ kết nối, chuyển dữ liệu và hiển thị kết quả. Hóa đơn tự động không thay khách hàng quyết định nội dung hóa đơn và không chịu trách nhiệm về nghĩa vụ thuế của khách hàng.

Người mua và khách hàng có thể tra cứu hóa đơn qua đường dẫn, mã tra cứu được gửi kèm hóa đơn, hoặc trên cổng thông tin của cơ quan thuế. Hóa đơn được lưu trữ theo quy định của pháp luật về hóa đơn và kế toán.

## Điều 9. Quyền và nghĩa vụ của khách hàng

Khách hàng có quyền:

- Sử dụng tính năng theo gói dịch vụ đã mua.
- Được hỗ trợ kỹ thuật qua [EMAIL HỖ TRỢ] và [HOTLINE] trong giờ làm việc.
- Xem, xuất và tải về dữ liệu khách hàng, sản phẩm, giao dịch và hóa đơn của mình.
- Khiếu nại về chất lượng dịch vụ và yêu cầu hoàn tiền theo Điều 6.

Khách hàng có nghĩa vụ:

- Thanh toán phí đầy đủ, đúng hạn.
- Bảo đảm có cơ sở pháp lý khi nhập dữ liệu cá nhân của người mua và người lao động lên nền tảng.
- Tuân thủ điều khoản này và pháp luật về thuế, hóa đơn, thanh toán, bảo vệ dữ liệu cá nhân, an ninh mạng.
- Tự sao lưu dữ liệu quan trọng định kỳ.

## Điều 10. Quyền và nghĩa vụ của Nhà cung cấp

Hóa đơn tự động có quyền:

- Thu phí theo gói dịch vụ đã công bố.
- Nâng cấp, thay đổi, bổ sung hoặc ngừng một tính năng, nếu không làm giảm đáng kể quyền lợi chính của gói khách hàng đã mua.
- Bảo trì hệ thống, có thông báo trước trên nền tảng hoặc qua email trong trường hợp có thể.
- Tạm ngừng hoặc chấm dứt dịch vụ theo Điều 13.

Hóa đơn tự động có nghĩa vụ:

- Duy trì dịch vụ ổn định, khắc phục sự cố trong thời gian hợp lý.
- Áp dụng biện pháp kỹ thuật và quản lý phù hợp để bảo vệ hệ thống và dữ liệu.
- Bảo mật thông tin khách hàng, chỉ cung cấp cho bên thứ ba theo Điều 12.
- Thông báo cho khách hàng khi có sự cố an ninh ảnh hưởng đến dữ liệu của họ.
- Giải quyết khiếu nại của khách hàng kịp thời, công khai quy trình hỗ trợ.

## Điều 11. Các hành vi bị cấm

Khách hàng không được:

- Đăng ký bằng thông tin giả, mạo danh tổ chức hoặc cá nhân khác.
- Lập hóa đơn khống, hóa đơn không có giao dịch thật, hoặc dùng hóa đơn để trốn thuế, hợp thức hóa hàng hóa, dịch vụ trái phép.
- Dùng tính năng thu tiền để rửa tiền, lừa đảo, cờ bạc, kinh doanh hàng cấm hoặc giao dịch trái pháp luật khác.
- Thu thập, mua bán, chia sẻ trái phép dữ liệu cá nhân của người khác.
- Truy cập trái phép, dò quét, tấn công, phát tán mã độc, can thiệp vào hệ thống hoặc tài khoản của người khác, trái với Luật An ninh mạng số 116/2025/QH15.
- Sao chép, dịch ngược, bán lại hoặc cho thuê lại nền tảng khi chưa có chấp thuận bằng văn bản của Hóa đơn tự động.

## Điều 12. Bảo vệ dữ liệu cá nhân

Hóa đơn tự động xử lý dữ liệu cá nhân theo Luật Bảo vệ dữ liệu cá nhân số 91/2025/QH15 và Nghị định số 356/2025/NĐ-CP quy định chi tiết một số điều và biện pháp thi hành Luật này.

Dữ liệu được xử lý gồm: thông tin tài khoản, thông tin người đại diện, số tài khoản ngân hàng liên kết, dữ liệu giao dịch, dữ liệu người mua do khách hàng nhập và nhật ký truy cập.

Mục đích xử lý: tạo và quản lý tài khoản, cung cấp tính năng thu tiền và hóa đơn, thanh toán phí, hỗ trợ khách hàng, bảo đảm an ninh hệ thống và thực hiện nghĩa vụ theo yêu cầu của cơ quan nhà nước có thẩm quyền.

Đối với dữ liệu của người mua và người lao động do khách hàng đưa lên nền tảng, khách hàng là bên kiểm soát dữ liệu cá nhân và phải bảo đảm có sự đồng ý hoặc căn cứ pháp lý khác; Hóa đơn tự động là bên xử lý dữ liệu cá nhân, chỉ xử lý theo chỉ dẫn của khách hàng và để cung cấp dịch vụ.

Hóa đơn tự động chỉ chia sẻ dữ liệu cần thiết cho:

- Đối tác thanh toán, để nhận thông báo biến động số dư.
- Nhà cung cấp dịch vụ hóa đơn điện tử và cơ quan thuế, để lập, gửi và cấp mã hóa đơn.
- Cơ quan nhà nước có thẩm quyền, khi pháp luật yêu cầu.

Hóa đơn tự động không mua bán dữ liệu cá nhân. Chủ thể dữ liệu có quyền được biết, đồng ý hoặc rút lại sự đồng ý, xem, chỉnh sửa, yêu cầu xóa, hạn chế xử lý và khiếu nại theo pháp luật, bằng cách gửi yêu cầu đến [EMAIL HỖ TRỢ]. Việc rút lại sự đồng ý không ảnh hưởng đến dữ liệu mà pháp luật buộc phải lưu giữ, như hóa đơn và chứng từ kế toán.

Khi tài khoản chấm dứt, dữ liệu được lưu giữ trong thời hạn pháp luật yêu cầu, sau đó được xóa hoặc ẩn danh.

## Điều 13. Tạm ngừng và chấm dứt dịch vụ

Khách hàng có thể ngừng sử dụng và yêu cầu đóng tài khoản bất kỳ lúc nào. Phí đã thanh toán được xử lý theo Điều 6.

Hóa đơn tự động có thể tạm ngừng hoặc chấm dứt tài khoản khi:

- Khách hàng vi phạm Điều 11 hoặc vi phạm nghiêm trọng nghĩa vụ khác và không khắc phục sau khi được thông báo.
- Có yêu cầu của cơ quan nhà nước có thẩm quyền.
- Có dấu hiệu tài khoản bị chiếm đoạt hoặc bị dùng cho giao dịch gian lận, nhằm bảo vệ khách hàng và người mua.
- Gói dịch vụ hết hạn quá [SỐ NGÀY LƯU TÀI KHOẢN] ngày mà khách hàng không gia hạn.

Trước khi chấm dứt, trừ trường hợp khẩn cấp hoặc theo yêu cầu của cơ quan có thẩm quyền, Hóa đơn tự động thông báo qua email và tạo điều kiện để khách hàng tải về dữ liệu, hóa đơn của mình. Việc chấm dứt không làm mất hiệu lực của các hóa đơn đã lập.

## Điều 14. Giới hạn trách nhiệm

Hóa đơn tự động không chịu trách nhiệm đối với:

- Thông tin sai do khách hàng nhập hoặc cấu hình, kể cả hóa đơn lập sai do dữ liệu đầu vào.
- Sự cố, chậm trễ của ngân hàng, đối tác thanh toán, nhà cung cấp dịch vụ hóa đơn điện tử, hệ thống của cơ quan thuế hoặc mạng viễn thông nằm ngoài khả năng kiểm soát hợp lý của Hóa đơn tự động.
- Thiệt hại do khách hàng để lộ mật khẩu hoặc không tuân thủ hướng dẫn bảo mật.
- Sự kiện bất khả kháng theo Bộ luật Dân sự số 91/2015/QH13.
- Nghĩa vụ thuế, tiền phạt hoặc tranh chấp giữa khách hàng và người mua.

Trong mọi trường hợp, tổng mức bồi thường của Hóa đơn tự động cho mỗi sự việc không vượt quá tổng phí khách hàng đã thanh toán cho gói dịch vụ trong 12 tháng gần nhất, trừ trường hợp pháp luật có quy định khác hoặc thiệt hại do lỗi cố ý của Hóa đơn tự động.

## Điều 15. Thay đổi điều khoản và giải quyết tranh chấp

Hóa đơn tự động có thể sửa đổi điều khoản này. Bản sửa đổi được đăng trên nền tảng và thông báo qua email ít nhất [SỐ NGÀY THÔNG BÁO] ngày trước khi áp dụng, trừ thay đổi do yêu cầu bắt buộc của pháp luật. Nếu không đồng ý, khách hàng có thể ngừng sử dụng và được xử lý phí theo Điều 6. Việc tiếp tục sử dụng sau ngày áp dụng được xem là chấp nhận bản sửa đổi.

Điều khoản này được điều chỉnh bởi pháp luật Việt Nam, gồm Bộ luật Dân sự số 91/2015/QH13, Luật Thương mại số 36/2005/QH11 (đã được sửa đổi, bổ sung) và, khi khách hàng là người tiêu dùng, Luật Bảo vệ quyền lợi người tiêu dùng số 19/2023/QH15.

Tranh chấp được giải quyết trước hết bằng thương lượng, hòa giải. Khách hàng gửi khiếu nại đến [EMAIL HỖ TRỢ]; Hóa đơn tự động phản hồi trong vòng [SỐ NGÀY PHẢN HỒI KHIẾU NẠI] ngày làm việc. Nếu không thương lượng được trong 30 ngày kể từ ngày một bên gửi yêu cầu, một trong hai bên có quyền khởi kiện tại Tòa án nhân dân có thẩm quyền theo pháp luật Việt Nam.

## Điều 16. Thông tin nhà cung cấp và hiệu lực

Dịch vụ Hóa đơn tự động do [TÊN CÔNG TY] cung cấp.

- Mã số thuế: [MST]
- Địa chỉ: [ĐỊA CHỈ]
- Email hỗ trợ: [EMAIL HỖ TRỢ]
- Hotline: [HOTLINE]
- Website: hoadon.tuvangiaiphapta.vn và hoadontudong.vn

Điều khoản này có hiệu lực từ ngày [NGÀY HIỆU LỰC]. Nếu một nội dung bị vô hiệu theo pháp luật, các nội dung còn lại vẫn giữ nguyên hiệu lực.
""";
}
