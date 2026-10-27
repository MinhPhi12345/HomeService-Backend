namespace HomeService.Api.DTOs;

// NFR-16 – GET /api/auditlogs: một dòng nhật ký kiểm toán (ai, khi nào, số tiền)
public record AuditLogItem(
    long Id,
    string Action,
    int? BookingId,
    int? PaymentId,
    int? ActorUserId,
    string ActorName,   // "Hệ thống" khi do tác vụ nền thực hiện
    decimal Amount,
    string? Detail,
    DateTime CreatedAt
);
