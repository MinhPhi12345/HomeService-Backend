namespace HomeService.Api.DTOs;

// POST /api/uploads (SRS 9.11) – Url là đường dẫn tương đối "/uploads/yyyyMM/ten.png";
// FE / Mobile ghép với địa chỉ Backend (bỏ phần "/api") khi hiển thị ảnh.
public record UploadResponse(string Url);
