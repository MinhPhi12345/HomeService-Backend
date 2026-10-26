# Đề xuất cập nhật SRS v1.3 → v1.4 (phía Backend)

Theo quy tắc của SRS ("code phải theo SRS; muốn khác thì cập nhật SRS trước"), dưới đây là các điểm Backend đã **bổ sung** trong quá trình làm 7 Sprint. Tất cả đều là **thêm mới** (field thêm vào cuối DTO, endpoint mới, query tùy chọn) — không đổi tên/xóa field nào đã có, nên FE đang code theo v1.3 không bị ảnh hưởng. Nhóm xem lại, đồng ý thì đưa vào SRS v1.4.

## 1. Endpoint mới

| Endpoint | Quyền | Mục đích | US |
|---|---|---|---|
| `GET /api/reviews/user/{userId}` | Auth | Tổng hợp đánh giá một người nhận được (số lượng, điểm TB, 50 đánh giá gần nhất) | US26 |
| `DELETE /api/reviews/{id}` | A | Admin gỡ đánh giá bất thường, tính lại điểm TB Tasker | US27 |
| `PUT /api/servicecategories/{id}/restore` | A | Khôi phục dịch vụ đã xóa mềm | US09 |
| `PUT /api/users/{id}/cash-blocked?isCashBlocked=` | A | Admin mở lại/chặn thanh toán tiền mặt của khách (BR-U01 "Admin có thể mở lại") | US36 |
| `GET /api/geo/tiles/{z}/{x}/{y}.png` | Không cần | Ảnh nền bản đồ OpenStreetMap đi qua Backend (cache 14 ngày, User-Agent riêng) – app/web không cần API key | US13, US19 |
| `GET /api/geo/reverse?lat=&lng=` | Auth | Toạ độ ⇒ địa chỉ gọn (Nominatim) để tự điền ô địa chỉ | US13 |
| `GET /api/geo/search?q=` | Auth | Địa chỉ ⇒ danh sách vị trí ở Việt Nam (tối đa 6) | US13 |

## 2. Query tùy chọn mới (không gửi thì hoạt động như cũ)

| API | Query | Ý nghĩa |
|---|---|---|
| `GET /api/bookings/my` | `status=2,3` · `upcoming=true` · `keyword=` | Lọc trạng thái; đơn sắp làm sắp theo giờ hẹn; tìm theo tên dịch vụ/địa chỉ |
| `GET /api/offers/my` | `all=true` | Xem cả lịch sử lời mời (mặc định chỉ lời mời đang chờ) |
| `GET /api/users` | `keyword=` · `isActive=` | Tìm theo tên/email/SĐT, lọc đang hoạt động/bị khóa |
| `GET /api/servicecategories` | `includeInactive=true` (chỉ Admin) | Xem cả dịch vụ đã xóa mềm |
| `GET /api/notifications/my` | `unreadOnly=true` | Chỉ thông báo chưa đọc |
| `GET /api/reports/revenue` | `groupBy=day` / `month` / `year` | Chuỗi số liệu cho biểu đồ (giờ Việt Nam) |
| `GET /api/reports/tasker-performance` | `from=` · `to=` | Lọc theo giờ hẹn |
| `GET /api/reports/*` | `from`, `to` bỏ trống | Mặc định 30 ngày gần nhất |

## 3. Field bổ sung (thêm vào cuối DTO)

| DTO | Field mới |
|---|---|
| `CreateBookingRequest` | `contactPhone` (tùy chọn, `^0\d{9}$`): SĐT để Tasker liên hệ cho đơn; bỏ trống => lấy SĐT hồ sơ lúc đặt |
| `BookingResponse` | `serviceCategoryId, note, quantity, latitude, longitude, updatedAt, matchRound, currentWave, cancellationFee, disputeId, customerPhone, taskerPhone, canReview, contactPhone` (`customerPhone/taskerPhone` chỉ có giá trị với 2 bên của đơn khi đơn đang 2/3; `customerPhone` ưu tiên số liên hệ của đơn; `contactPhone` khách luôn thấy, Tasker thấy khi đơn 2/3) |
| `OfferResponse` | `wave, note, quantity, customerRatingAverage` |
| `PendingTaskerItem` | `email, phoneNumber, idCardImageUrl, baseAddress, baseLatitude, baseLongitude, serviceRadiusKm, submittedAt` |
| `AdminUserItem` | `outstandingAmount, isCashBlocked, unpaidReportCount, verificationStatus, ratingAverage, violationPoints, suspendedUntil` (null nếu không phải Tasker) |
| `ProfileResponse` | `outstandingAmount, isCashBlocked, createdAt` |
| `ReviewResponse` | `revieweeName` |
| `LocationResponse` | `distanceKm` (khoảng cách Tasker → địa chỉ đơn) |
| `DisputeResponse` | `customerId, taskerId, redoScheduledTime, taskerResponseDueAt` |
| `RevenueReport` | `taskerPayout, groupBy, series[] { period, revenue, commission, transactionCount }` |
| `TaskerPerformanceItem` | `taskerName, ratingAverage, acceptedOffers, totalOffers, acceptanceRate, noShowCount, disputeCount, violationPoints, totalEarnings` |
| `GET /notifications/my` | ngoài `items, page, pageSize, totalCount` có thêm `unreadCount` |

## 4. Bảng/cột mới trong database

- Bảng `AuditLogs` (NFR-16): `Id, Action, BookingId, PaymentId, ActorUserId, Amount, Detail, CreatedAt`.
- `Bookings`: cột `ContactPhone varchar(15)` (SĐT liên hệ của đơn) và 3 cột nội bộ (không trả ra API) `AutoConfirmReminderSentAt` (đã nhắc trước hạn tự xác nhận), `NoShowAt` (mốc tự hủy 2 giờ sau khi Tasker không đến), `ReminderSentAt` (đã nhắc lịch hẹn). Các cột "dự kiến" khác trong SRS 7.4 đã được tạo đúng tên.
- `TaskerProfiles`: `SubmittedAt` (sắp xếp danh sách chờ duyệt).
- `Disputes`: `TaskerRespondedAt`.
- `Reviews`: UNIQUE `(BookingId, ReviewerId)` và CHECK `Rating BETWEEN 1 AND 5`.

## 5. Quy tắc nghiệp vụ cần ghi rõ trong SRS

1. **Khớp kỹ năng (BR-M01):** kỹ năng khớp dịch vụ khi tên dịch vụ chứa kỹ năng hoặc ngược lại, không phân biệt hoa thường (vd: "Dọn dẹp" khớp "Dọn dẹp nhà").
2. **Hồ sơ Tasker chờ duyệt:** chỉ hiện trong `pending-verification` khi Tasker đã gửi CCCD. Hồ sơ bị từ chối gửi lại → về chờ duyệt.
3. **Tasker không đến – Tìm Tasker khác:** nếu khách không gửi `scheduledTime`, giờ hẹn mới = hiện tại + 1 giờ (làm tròn 5 phút) vì giờ hẹn cũ đã qua.
4. **Tasker đã đến (phí hủy 50%):** vị trí gửi gần nhất trong 30 phút cách địa chỉ đơn ≤ 200 m (`Settlement:ArrivedLocationFreshMinutes`).
5. **Trả nợ phí hủy đơn tiền mặt:** khách trả qua `POST /api/payments` với `bookingId` của đơn đã hủy; tiền chuyển toàn bộ cho Tasker (không chiết khấu).
6. **Khiếu nại "khách chưa thanh toán" (FR-27 bước 4):** đơn 13 khiếu nại được trong 24 giờ; Admin chỉ chọn `0` (bác – giữ nợ) hoặc `2` (hủy nợ – khách đã trả tiền mặt: đơn → 5, trừ chiết khấu từ ví Tasker, Tasker +2 điểm vi phạm).
7. **Điểm đánh giá:** đánh giá đang bị gắn cờ tạm không tính vào `ratingAverage`.
8. **Doanh thu (US31):** ghi nhận theo thời điểm tiền được chia (giải ngân / hoàn / thu tiền mặt); `totalRevenue = Σ(amount − refundedAmount)` của Payment trạng thái 1, 4, 6.
9. **Đồng thời:** 2 thao tác cùng sửa 1 đơn/ví/thanh toán → thao tác đến sau nhận `409 { "message": "Dữ liệu vừa được cập nhật bởi thao tác khác, vui lòng tải lại và thử lại." }`.
10. **Đường dẫn chữ thường:** Backend bật `LowercaseUrls` nên Swagger và header `Location` dùng `/api/bookings/...`.
