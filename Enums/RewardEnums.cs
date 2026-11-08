namespace HomeService.Api.Enums;

// SRS 7.8 – EP09 Khen thưởng Tasker (FR-31)
public enum TaskerAwardStatus
{
    Proposed = 0, // Chờ duyệt (đã chốt bảng xếp hạng, Admin chưa duyệt)
    Approved = 1, // Đã duyệt – đã trao thưởng
    Excluded = 2  // Admin loại khỏi danh sách (kèm lý do)
}

// SRS 7.8 – EP10 Khuyến mãi khách hàng (FR-32, FR-33)
public enum VoucherDiscountType
{
    Percent = 0,     // Giảm theo % (có mức giảm tối đa)
    FixedAmount = 1  // Giảm số tiền cố định
}

public enum VoucherAudience
{
    AllCustomers = 0, // Mọi khách hàng
    NewCustomers = 1  // Chỉ khách chưa có đơn hoàn tất nào
}

public enum VoucherSource
{
    Campaign = 0, // Chương trình Admin tạo
    Welcome = 1,  // Quà chào mừng (xác thực email lần đầu)
    Loyalty = 2,  // Quà khách thân thiết (mỗi N đơn hoàn tất)
    Apology = 3   // Quà xin lỗi (Tasker không đến / khiếu nại được chấp nhận)
}

public enum VoucherRedemptionStatus
{
    Applied = 0,  // Đang giữ – mã đã gắn vào đơn đang xử lý
    Used = 1,     // Đã dùng – đơn đã hoàn tất (hoặc hủy có phí)
    Released = 2  // Đã trả lại lượt – đơn bị hủy / hết hạn mà không thực hiện
}
