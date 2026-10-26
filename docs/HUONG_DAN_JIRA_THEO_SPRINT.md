# Hướng dẫn cập nhật Jira theo từng Sprint – Backend HomeService

Tài liệu đi kèm code Backend. Mỗi Sprint có: cách **Start sprint**, các **Story + Sub-task Backend** cần thêm, **nhánh/commit** tương ứng, **folder Postman** để kiểm thử và **điều kiện chuyển Done** (SRS mục 13).

---

## Phần A – Thao tác Jira dùng chung cho mọi Sprint

### A1. Chuẩn bị board (làm 1 lần)
1. Vào project **HOMESERVICE** → **Board**.
2. Thêm cột **TESTING** giữa *In Progress* và *Done* (đúng quy trình Backlog: TO DO → IN PROGRESS → TESTING → DONE):
   - Bấm **⋯** (góc phải board) → **Configure board / Board settings** → **Columns** → **Add column** → đặt tên `TESTING` → kéo vào giữa.
   - Nếu project kiểu *team-managed*: bấm dấu **+** ngay cạnh cột cuối, gõ `TESTING`, rồi kéo cột vào đúng vị trí.
3. Kiểm tra mỗi Story đã có field **Story point estimate** (điền SP theo Backlog: US02 = 3, US16 = 13…).

### A2. Tạo & bắt đầu Sprint
1. Mở **Backlog**. Ở khung Sprint, bấm **Create sprint** (nếu chưa có).
2. **Kéo các Story** của Sprint đó từ *Backlog* lên khung sprint (danh sách ở Phần B).
3. Bấm **Start sprint** trên đầu khung sprint, điền:
   - **Sprint name**: `HOMESERVICE Sprint N`
   - **Duration**: 2 weeks · **Start date**: ngày bắt đầu
   - **Sprint goal**: copy câu *Sprint Goal* ở Phần B
4. Bấm **Start** → board hiện các thẻ ở cột **TO DO**.

### A3. Thêm Sub-task cho Story
1. Bấm vào thẻ Story (vd: `US02 – Đăng nhập`) → trong khung chi tiết bấm **Add subtask / Create subtask** (biểu tượng ☑︎+).
2. Gõ tên sub-task (bảng ở Phần B), Enter. Lặp lại cho từng sub-task.
3. Mở từng sub-task: **Assignee** = người làm (sub-task Backend → Phi; màn hình Web/Mobile → bạn phụ trách FE), điền **Original estimate** nếu GV yêu cầu.
4. Nên ghi vào phần **Description** của Story: endpoint + request/response (theo checklist Phụ lục A của SRS) và **tên nhánh/commit** — GV dễ đối chiếu.

### A4. Thay đổi trạng thái khi làm
| Khi nào | Thao tác |
|---|---|
| Bắt đầu làm sub-task/Story (checkout nhánh, build, đọc code) | Kéo thẻ sang **IN PROGRESS** (hoặc mở thẻ → nút trạng thái → *In Progress*) |
| Code xong, đang chạy Postman / Swagger | Kéo sang **TESTING** |
| Test đạt đủ tiêu chí ở Phần B và PR đã được review | Kéo sang **DONE** |
| Test lỗi | Kéo về **IN PROGRESS**, thêm comment mô tả lỗi (hoặc tạo issue *Bug* gắn vào Story) |

> Story chỉ chuyển **DONE** khi **tất cả sub-task đã DONE**. Ghi comment kết quả test (ảnh chụp Postman) vào Story để làm minh chứng.

### A5. Kết thúc Sprint
1. Board → **Complete sprint** (góc phải trên).
2. Story chưa xong → chọn chuyển sang Sprint kế tiếp.
3. Vào **Reports → Burndown chart / Sprint report** chụp ảnh đưa vào báo cáo.

### A6. Lấy code của từng Sprint về máy
Code đã được tạo thành **7 nhánh nối tiếp nhau**, mỗi User Story là 1 commit (`feat: USxx ...`), cuối mỗi Sprint có 1 commit migration. Cách lấy và đẩy lên GitHub xem file `HUONG_DAN_LAY_CODE.md` (đặt cạnh thư mục `HomeService-Backend`). Tóm tắt mỗi Sprint:

```bash
git checkout feature/sprint-N-...      # nhánh của Sprint N
dotnet build                           # phải 0 lỗi
dotnet run   (hoặc docker compose up --build)
# → chạy folder "Sprint N" trong Postman
git push -u origin feature/sprint-N-...
# → GitHub: tạo Pull Request vào main, nhờ 1 bạn review rồi Merge
```

---

## Phần B – Nội dung từng Sprint

### Sprint 1 – Nền tảng tài khoản & danh mục dịch vụ (21 SP)
**Sprint goal:** Người dùng đăng ký/đăng nhập/đăng xuất được, quản trị viên xác thực hồ sơ Tasker, khách hàng xem được danh mục dịch vụ kèm bảng giá.
**Nhánh:** `feature/sprint-1-tai-khoan-dich-vu` · **Postman:** folder *Sprint 1*

| Story | Sub-task Backend (gợi ý tên) | Commit |
|---|---|---|
| US02 Đăng nhập (3) | T02 Thiết kế API Đăng nhập · T03 Kiểm tra tài khoản/mật khẩu, chặn tài khoản khóa · T04 Phân quyền theo vai trò trong JWT · T05 Test đăng nhập (Postman) | `chore: nền tảng Backend…`, `feat: US02…` |
| US01 Đăng ký (3) | T07 Database User · T08 API đăng ký · T09 Validate email/SĐT/mật khẩu theo SRS 6.1 · T10 Test đăng ký | `feat: US01…` |
| US04 Xác thực hồ sơ Tasker (5) | T12 API tải lên & lưu hồ sơ (`POST /uploads`, `POST/GET /taskers/profile`) · T14 API duyệt/từ chối + thông báo · T15 Test luồng xác thực | `feat: US04…` |
| US07 Thêm danh mục (3) | API thêm dịch vụ + validate tên/giá/đơn vị · Test | `feat: US07 US11…` |
| US10 Xem/Tìm kiếm dịch vụ (3) | API danh sách public + tìm theo từ khóa · Test | `feat: US10…` |
| US11 Thiết lập bảng giá (3) | API PUT cập nhật giá (giá chỉ áp dụng đơn mới) · Test | `feat: US07 US11…` |
| US03 Đăng xuất (1) | API `POST /auth/logout` (đã có) · FE xóa token · Test | *(không đổi code BE)* |

**Chuyển Done khi:** đăng ký email mới nhận token; email trùng → "Email đã được sử dụng."; sai mật khẩu → thông báo chung; tài khoản khóa → "Tài khoản đã bị khóa."; Tasker gửi hồ sơ → Admin thấy và duyệt được; thiếu điểm làm việc/bán kính → 400; tìm "dọn" chỉ ra dịch vụ chứa "dọn".

### Sprint 2 – Đặt dịch vụ & ghép nối Tasker theo đợt (24 SP)
**Sprint goal:** Khách hàng tạo được đơn (thời gian, địa chỉ có tọa độ, phương thức thanh toán); hệ thống tự động gửi lời mời cho Tasker theo đợt 3/5/8 km; quản trị viên sửa được danh mục dịch vụ.
**Nhánh:** `feature/sprint-2-dat-don-ghep-noi` · **Postman:** folder *Sprint 2* (+ request thanh toán ở *Sprint 3* để thấy ghép nối)

| Story | Sub-task Backend | Commit |
|---|---|---|
| US12 Tạo đơn (5) | Bổ sung model Booking (quantity, paymentMethod, commissionRate…) · API `POST /bookings` · Điều kiện tiền mặt BR-P06, chặn khách đang nợ · Test | `feat: US12 US13…` |
| US13 Chọn thời gian & địa chỉ (3) | Validate giờ hẹn (≥ 1 giờ, ≤ 30 ngày), tọa độ bắt buộc · Test | `feat: US12 US13…` |
| US16 Ghép nối theo đợt (13) | Bảng BookingOffers · Lọc cứng BR-M01 · Chấm điểm 40/30/20/10 · BackgroundService 3 đợt 45/45/60 giây · Thử lại đơn đặt trước 30 phút / NoTaskerFound · API `PUT /taskers/availability`, `GET /offers/my` · Test | `feat: US16…` |
| US08 Sửa danh mục (3) | Chặn trùng tên khi thêm/sửa · Test | `feat: US08…` |

**Chuyển Done khi:** đơn online → "Chờ thanh toán" (10); khách mới chọn tiền mặt bị chặn; giá = giá cơ bản × số lượng; giờ quá khứ/thiếu vị trí bị chặn; đơn đã thanh toán → chỉ Tasker trong 3 km nhận lời mời, hết 45 giây sang đợt 5 km, không gửi lại Tasker cũ; Tasker ngoài bán kính phục vụ không nhận lời mời.

> Test riêng nhánh này (chưa có API thanh toán): chạy SQL `UPDATE Bookings SET Status = 0 WHERE Id = <id>` để giả lập đã thanh toán.

### Sprint 3 – Nhận đơn, thanh toán trước & xác nhận hoàn thành (21 SP)
**Sprint goal:** Tasker nhận lời mời (người nhận trước được đơn); khách hàng thanh toán trước và tiền được tạm giữ; Tasker cập nhật trạng thái kèm ảnh; khách hàng xác nhận hoàn thành (hoặc tự xác nhận sau 24 giờ) để giải ngân.
**Nhánh:** `feature/sprint-3-nhan-don-thanh-toan` · **Postman:** folder *Sprint 3*

| Story | Sub-task Backend | Commit |
|---|---|---|
| US17 Nhận/Từ chối (5) | API accept/reject theo lời mời · Chốt người nhận nguyên tử (2 Tasker bấm cùng lúc chỉ 1 người được) · Tạm giữ chiết khấu đơn tiền mặt · Test | `feat: US17…` |
| US21 Thanh toán trước (8) | Bảng Payments mở rộng (Held, Released…) · API `POST /payments` (mô phỏng) · Tự hủy đơn quá 15 phút · Test | `feat: US21…` |
| US18 Cập nhật trạng thái (5) | API `PUT /bookings/{id}/status` kèm ảnh · Bảng chuyển trạng thái 10.2 · Đơn tiền mặt quyết toán khi đã thu · Test | `feat: US18…` |
| US34 Xác nhận & giải ngân (3) | API `POST /bookings/{id}/confirm` · Tự xác nhận sau 24 giờ + nhắc trước 2 giờ · Ví Tasker + lịch sử ví · Test | `feat: US34…` |

**Chuyển Done khi:** 2 Tasker nhận cùng lúc → 1 người 204, người kia 409; phải có ảnh mới Bắt đầu/Hoàn thành; không nhảy 2 → 4; đơn 300.000 đ: sau thanh toán Payment "Đang tạm giữ", xác nhận → ví Tasker +240.000 đ, chiết khấu 60.000 đ; tự xác nhận đúng 1 lần.

### Sprint 4 – Hoa hồng, theo dõi vị trí & thông báo đơn mới (23 SP)
**Sprint goal:** Tính hoa hồng khi giải ngân, khách hàng theo dõi vị trí Tasker, xử lý khi không có Tasker nhận đơn, hoàn thiện phân quyền và thông báo đơn mới cho Tasker.
**Nhánh:** `feature/sprint-4-hoa-hong-vi-tri-thong-bao` · **Postman:** folder *Sprint 4*

| Story | Sub-task Backend | Commit |
|---|---|---|
| US22 Hoa hồng (5) | Tính chiết khấu tập trung, làm tròn đến đồng · Kiểm tra bất biến tiền · Bảng AuditLogs (log kiểm toán) · Báo số dư ví thấp · Test | `feat: US22…` |
| US19 Theo dõi vị trí (5) | API `PUT/GET /bookings/{id}/location` (polling) · Test | `feat: US19…` |
| US33 Không có Tasker nhận (5) | API `POST /bookings/{id}/retry` (đổi giờ, tối đa 5 vòng) · Hủy hoàn 100% · Tự hết hạn sau 24 giờ · Test | `feat: US33…` |
| US06 Phân quyền (5) | Rà soát `[Authorize(Roles)]` toàn bộ API · Từ chối token của tài khoản bị khóa · Test 401/403 | `feat: US06…` |
| US28 Thông báo đơn mới (3) | API `GET /notifications/my` (phân trang), đánh dấu đã đọc · Test | `feat: US28…` |

**Chuyển Done khi:** đơn 123.457 đ → chiết khấu 24.691 đ; khách thấy vị trí Tasker; hết 3 đợt → "Chưa có Tasker nhận", Thử lại → "Đang tìm Tasker", Hủy → hoàn 100%, quá 24 giờ → "Đã hết hạn"; khách gọi API Admin → 403; Tasker thấy thông báo "Bạn có đơn mới".

### Sprint 5 – Quản trị người dùng, hủy đơn & khiếu nại (24 SP)
**Sprint goal:** Quản trị viên khóa/mở khóa tài khoản; khách hàng đánh giá Tasker; Tasker xem đơn đã nhận; hủy đơn có phí và hoàn tiền; khiếu nại và xử lý tranh chấp.
**Nhánh:** `feature/sprint-5-quan-tri-huy-don-khieu-nai` · **Postman:** folder *Sprint 5*

| Story | Sub-task Backend | Commit |
|---|---|---|
| US30 Quản lý KH & Tasker (5) | API danh sách lọc vai trò/từ khóa · Khóa/mở khóa (không tự khóa mình) · Test | `feat: US30…` |
| US25 Đánh giá Tasker (3) | API `POST /reviews` (1 lần/đơn) · Cập nhật điểm TB · Test | `feat: US25…` |
| US20 Xem đơn đã nhận (3) | `GET /bookings/my?status=&upcoming=` · SĐT liên hệ · Test | `feat: US20…` |
| US14 Hủy đơn, phí hủy (5) | Tính phí BR-C01 (0 / 20% / 50%) · API `GET /bookings/{id}/cancellation-fee` · Hoàn tiền + phí cho Tasker · Test | `feat: US14…` |
| US35 Khiếu nại (8) | Bảng Disputes · API khiếu nại/phản hồi/xử lý 4 hướng · Điểm vi phạm & tạm khóa 7 ngày · Test | `feat: US35…` |

**Chuyển Done khi:** khóa tài khoản → không đăng nhập được, mở khóa → được; hủy khi Tasker đã nhận & còn < 2 giờ → khách hoàn 80%, ví Tasker +20%; đơn đang thực hiện không hủy được; khiếu nại trong 24 giờ → "Đang khiếu nại", không tự giải ngân; Admin hoàn 30% đơn 450.000 đ → khách 135.000 đ, Tasker 252.000 đ, chiết khấu 63.000 đ.

### Sprint 6 – Chức năng phụ trợ (24 SP)
**Sprint goal:** Xử lý Tasker không đến, lịch sử đặt dịch vụ và giao dịch, ví Tasker (nạp/rút), đánh giá khách hàng và xử lý đánh giá bất thường.
**Nhánh:** `feature/sprint-6-chuc-nang-phu-tro` · **Postman:** folder *Sprint 6*

| Story | Sub-task Backend | Commit |
|---|---|---|
| US37 Tasker không đến (5) | Job phát hiện quá 30 phút · Tìm Tasker khác (loại Tasker cũ) / Hủy hoàn 100% · Tự hủy sau 2 giờ · Test (SQL chỉnh giờ hẹn) | `feat: US37…` |
| US15 Lịch sử đặt dịch vụ (3) | `GET /bookings/my?keyword=` · cờ `canReview` · Test | `feat: US15…` |
| US23 Ví Tasker (5) | API `GET /wallet`, `POST /wallet/topup`, `POST /wallet/withdraw` · Test | `feat: US23…` |
| US24 Lịch sử giao dịch (3) | API `GET /payments/my`, `GET /wallet/transactions` (phân trang) · Test | `feat: US24…` |
| US26 Đánh giá khách (3) | Cho Tasker đánh giá khách · `GET /reviews/user/{id}` · điểm uy tín khách trong lời mời · Test | `feat: US26…` |
| US27 Đánh giá bất thường (5) | API báo cáo · Admin bỏ cờ/gỡ đánh giá, tính lại điểm · Test | `feat: US27…` |

**Chuyển Done khi:** quá 30 phút Tasker chưa bắt đầu → khách được báo, Tìm Tasker khác/Hủy hoàn 100%, Tasker +1 điểm vi phạm (đủ 3 bị khóa 7 ngày); rút quá số dư khả dụng → 400; ví không âm, mọi biến động có trong lịch sử; mỗi bên đánh giá 1 lần.

### Sprint 7 – Hoàn thiện hệ thống & báo cáo (24 SP)
**Sprint goal:** Thông báo trạng thái & nhắc lịch, cập nhật thông tin cá nhân, xóa danh mục, báo cáo thống kê cho quản trị viên và xử lý khách không thanh toán tiền mặt.
**Nhánh:** `feature/sprint-7-hoan-thien-bao-cao` (nhánh này chứa **toàn bộ** code + tài liệu) · **Postman:** folder *Sprint 7*

| Story | Sub-task Backend | Commit |
|---|---|---|
| US29 Thông báo & nhắc lịch (3) | Job nhắc lịch trước 1 giờ cho khách và Tasker · Rà soát thông báo đổi trạng thái · Test | `feat: US29…` |
| US05 Cập nhật thông tin cá nhân (3) | API `GET/PUT /profile` · Test | `feat: US05…` |
| US09 Xóa danh mục (3) | Xóa mềm, Admin xem dịch vụ đã xóa & khôi phục · Test | `feat: US09…` |
| US31 Báo cáo doanh thu (5) | API `GET /reports/revenue` (tiền mặt/online, tạm giữ, đã hoàn, theo ngày/tháng/năm) · Test | `feat: US31 US32…` |
| US32 Báo cáo đơn & hiệu suất (5) | API `bookings-summary` (status số), `tasker-performance` · Test | `feat: US31 US32…` |
| US36 Khách không thanh toán (5) | API `POST /bookings/{id}/report-unpaid` · Ghi nợ, chặn đặt đơn · Trả nợ online · Lần 2 chỉ được online · Khiếu nại hủy nợ · Test | `feat: US36…` |

**Chuyển Done khi:** Dashboard hiện đúng doanh thu/hoa hồng/số đơn theo trạng thái; Tasker báo khách chưa trả → khách không đặt đơn mới đến khi trả nợ, ví Tasker không bị trừ chiết khấu; lần 2 → khách chỉ được thanh toán online; dịch vụ đã xóa không hiện và không đặt được.
