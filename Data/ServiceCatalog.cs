using HomeService.Api.Models;

namespace HomeService.Api.Data;

/// <summary>
/// SRS v1.8 – cấu hình mặc định của 5 dịch vụ mẫu: số lượng, thời lượng ước tính, hạng mục và chi tiết dịch vụ.
/// Dùng khi tạo dữ liệu mẫu (DB mới). DB đã có dịch vụ được cập nhật bằng migration DichVuSoLuongVaKhoaTaiKhoan.
/// </summary>
public static class ServiceCatalog
{
    public static List<ServiceCategory> Defaults() => new()
    {
        new()
        {
            Name = "Dọn dẹp nhà", Description = "Quét dọn, lau nhà, vệ sinh bếp và nhà tắm theo giờ.", BasePrice = 150000, Unit = "giờ",
            MinQuantity = 2, MaxQuantity = 4, QuantityLabel = "Số giờ làm", QuantityHint = "2 giờ ≈ 55 m² (2 phòng) · 3 giờ ≈ 85 m² (3 phòng) · 4 giờ ≈ 105 m² (4 phòng)",
            MinutesPerUnit = 60, RequiresItemList = false,
            Includes = "Quét, hút bụi và lau sàn các phòng\nLau bụi bàn ghế, kệ tủ, cửa sổ trong tầm với\nVệ sinh bếp: mặt bếp, bồn rửa, mặt ngoài tủ bếp\nVệ sinh nhà tắm: bồn cầu, lavabo, gương, sàn\nThu gom và đổ rác",
            Excludes = "Giặt ủi quần áo, nấu ăn (đặt dịch vụ riêng)\nLau kính mặt ngoài tòa nhà, làm việc trên cao trên 2 m\nGiặt sofa, nệm, rèm bằng máy chuyên dụng\nDọn nhà sau xây dựng, sửa chữa",
            CustomerPrepares = "Chọn số giờ phù hợp với diện tích nhà\nChổi, cây lau nhà, khăn và nước lau sàn\nCất giữ đồ quý giá, giấy tờ quan trọng",
            Notes = "Tasker làm đúng số giờ đã đặt; cần thêm giờ thì đặt đơn mới\nNhà trên 105 m² nên đặt 2 đơn vào các buổi khác nhau",
        },
        new()
        {
            Name = "Giặt ủi", Description = "Giặt, sấy và ủi quần áo tại nhà.", BasePrice = 80000, Unit = "mẻ",
            MinQuantity = 1, MaxQuantity = 3, QuantityLabel = "Số mẻ giặt", QuantityHint = "1 mẻ ≈ 7 kg ≈ 15–20 bộ quần áo (máy giặt gia đình)",
            MinutesPerUnit = 90, RequiresItemList = false,
            Includes = "Phân loại quần áo theo màu và chất liệu\nGiặt bằng máy giặt của gia đình, phơi hoặc sấy khô\nỦi phẳng và gấp gọn quần áo",
            Excludes = "Giặt khô, giặt hấp đồ cao cấp (vest, áo dài, đồ da)\nGiặt chăn, nệm, rèm cửa cỡ lớn\nTẩy vết bẩn lâu ngày bằng hóa chất chuyên dụng",
            CustomerPrepares = "Máy giặt, nước giặt, nước xả và bàn ủi\nƯớc lượng số mẻ: 1 mẻ ≈ 7 kg ≈ 15–20 bộ quần áo",
            Notes = "Nhiều hơn 3 mẻ (khoảng 60 bộ quần áo) thì đặt thêm đơn vào ngày khác",
        },
        new()
        {
            Name = "Sửa điện nước", Description = "Sửa chữa điện, nước, thay thiết bị cơ bản.", BasePrice = 200000, Unit = "hạng mục",
            MinQuantity = 1, MaxQuantity = 5, QuantityLabel = "Số hạng mục cần sửa", QuantityHint = "Mỗi hạng mục là một chỗ cần sửa, ví dụ: 1 vòi nước rỉ, 1 ổ điện hỏng.",
            MinutesPerUnit = 60, RequiresItemList = true,
            Includes = "Kiểm tra và sửa vòi nước, đường ống nước lộ thiên bị rò rỉ\nThông tắc bồn rửa, lavabo, thoát sàn đơn giản\nSửa, thay ổ cắm, công tắc, bóng đèn, máng đèn\nKiểm tra chập điện, mất điện cục bộ trong nhà",
            Excludes = "Tiền linh kiện, vật tư thay thế (khách trả riêng theo giá thực tế)\nĐi lại đường điện, ống nước âm tường\nSửa thiết bị điện tử: tivi, tủ lạnh, máy giặt, máy lạnh",
            CustomerPrepares = "Ghi rõ từng hạng mục cần sửa khi đặt đơn\nNgắt cầu dao nếu đang có sự cố chập điện",
            Notes = "Tasker chụp ảnh trước và sau cho từng hạng mục, bạn kiểm tra ảnh trước khi xác nhận hoàn thành\nGiá là tiền công cho mỗi hạng mục",
        },
        new()
        {
            Name = "Chăm sóc cây", Description = "Tưới, cắt tỉa, bón phân cây cảnh và vườn nhỏ.", BasePrice = 120000, Unit = "buổi",
            MinQuantity = 1, MaxQuantity = 1, QuantityLabel = "Số buổi", QuantityHint = null,
            MinutesPerUnit = 90, RequiresItemList = false,
            Includes = "Tưới cây, nhổ cỏ, xới đất\nCắt tỉa cành lá hư, tạo dáng cơ bản\nBón phân, phun thuốc phòng sâu bệnh",
            Excludes = "Tiền phân bón, thuốc, chậu và đất (khách chuẩn bị)\nChặt hạ cây lớn, cây cao trên 3 m\nThiết kế, thi công sân vườn mới",
            CustomerPrepares = "Phân bón, thuốc, chậu và đất (nếu cần thay)\nVòi nước hoặc bình tưới",
            Notes = "Phù hợp ban công, sân thượng và vườn nhỏ dưới 50 m²",
        },
        new()
        {
            Name = "Nấu ăn gia đình", Description = "Đi chợ và nấu bữa ăn cho gia đình.", BasePrice = 180000, Unit = "buổi",
            MinQuantity = 1, MaxQuantity = 1, QuantityLabel = "Số buổi", QuantityHint = null,
            MinutesPerUnit = 120, RequiresItemList = false,
            Includes = "Đi chợ theo thực đơn và tiền chợ bạn đưa (khoảng 30 phút)\nSơ chế, nấu bữa ăn 3–4 món cho gia đình 2–6 người\nRửa dụng cụ, dọn gọn bếp sau khi nấu",
            Excludes = "Tiền mua thực phẩm (khách trả riêng)\nNấu tiệc, cỗ trên 6 người hoặc trên 4 món",
            CustomerPrepares = "Thực đơn mong muốn và tiền đi chợ (nếu nhờ Tasker đi chợ)\nDụng cụ nấu, gia vị cơ bản",
            Notes = "Ghi khẩu vị, món kiêng hoặc dị ứng thực phẩm trong ô Ghi chú",
        },
    };
}
