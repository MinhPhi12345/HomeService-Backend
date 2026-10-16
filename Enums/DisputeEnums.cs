namespace HomeService.Api.Enums;

// SRS 7.8 – khiếu nại (US35)
public enum DisputeReason
{
    PoorQuality = 0,    // Làm không đạt yêu cầu
    Incomplete = 1,     // Làm chưa xong / thiếu hạng mục
    PropertyDamage = 2, // Làm hư hỏng tài sản
    Behavior = 3,       // Thái độ không phù hợp
    Other = 4           // Khác
}

public enum DisputeStatus
{
    Open = 0,     // Chờ xử lý
    Resolved = 1  // Đã xử lý
}

public enum DisputeResolution
{
    ReleaseFull = 0,   // Bác khiếu nại – giải ngân đủ
    PartialRefund = 1, // Hoàn tiền một phần
    FullRefund = 2,    // Hoàn toàn bộ
    Redo = 3           // Làm lại miễn phí
}
