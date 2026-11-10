using System.Net;
using System.Security.Cryptography;
using System.Text;
using HomeService.Api.Common;
using HomeService.Api.Data;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using HomeService.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeService.Api.Services;

/// <summary>
/// Mã OTP 6 số gửi qua email cho: xác thực đăng ký, quên mật khẩu, đổi mật khẩu, rút tiền ví.
/// Quy tắc: mã hết hạn sau Otp:ExpiryMinutes phút; nhập sai tối đa Otp:MaxAttempts lần;
/// gửi lại sau Otp:ResendSeconds giây; tối đa Otp:MaxPerHour mã/giờ cho mỗi mục đích;
/// gửi mã mới thì mã cũ cùng mục đích hết hiệu lực; mã dùng xong không dùng lại được.
/// </summary>
public class OtpService
{
    private readonly AppDbContext _db;
    private readonly IEmailSender _email;
    private readonly OtpOptions _opt;
    private readonly ILogger<OtpService> _logger;

    public OtpService(AppDbContext db, IEmailSender email, IOptions<OtpOptions> options, ILogger<OtpService> logger)
    {
        _db = db;
        _email = email;
        _opt = options.Value;
        _logger = logger;
    }

    public int ExpiryMinutes => _opt.ExpiryMinutes;
    public int ResendSeconds => _opt.ResendSeconds;

    /// <summary>Tạo mã mới, lưu bản băm và gửi email. Lưu ý: hàm tự SaveChanges.</summary>
    public async Task<OtpSentResponse> SendAsync(User user, OtpPurpose purpose, decimal? amount = null)
    {
        var now = DateTime.UtcNow;
        var recent = await _db.OtpCodes
            .Where(o => o.UserId == user.Id && o.Purpose == purpose && o.CreatedAt > now.AddHours(-1))
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        var last = recent.FirstOrDefault();
        if (last is not null)
        {
            var wait = (int)Math.Ceiling((last.CreatedAt.AddSeconds(_opt.ResendSeconds) - now).TotalSeconds);
            if (wait > 0)
                throw ApiException.TooManyRequests($"Vui lòng đợi {wait} giây rồi gửi lại mã.");
        }
        if (recent.Count >= _opt.MaxPerHour)
            throw ApiException.TooManyRequests("Bạn đã yêu cầu quá nhiều mã OTP. Vui lòng thử lại sau 1 giờ.");

        // Mã cũ chưa dùng của cùng mục đích hết hiệu lực ngay
        foreach (var old in recent.Where(o => o.UsedAt == null && o.ExpiresAt > now))
            old.ExpiresAt = now;

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var otp = new OtpCode
        {
            UserId = user.Id,
            Purpose = purpose,
            CodeHash = Hash(user.Id, purpose, code),
            Amount = amount,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(_opt.ExpiryMinutes),
        };
        _db.OtpCodes.Add(otp);
        await _db.SaveChangesAsync();

        var (subject, html, text) = OtpEmail.Build(user.FullName, code, purpose, _opt.ExpiryMinutes, amount);
        try
        {
            await _email.SendAsync(user.Email, user.FullName, subject, html, text);
        }
        catch (Exception ex)
        {
            // Gửi lỗi: xóa mã vừa tạo để người dùng gửi lại ngay được (không tính vào thời gian chờ)
            _logger.LogError(ex, "Gửi email OTP {Purpose} tới {Email} thất bại", purpose, user.Email);
            _db.OtpCodes.Remove(otp);
            await _db.SaveChangesAsync();
            throw ApiException.ServiceUnavailable("Không gửi được email chứa mã OTP. Vui lòng thử lại sau ít phút.");
        }

        return new OtpSentResponse(MaskEmail(user.Email), otp.ExpiresAt, _opt.ResendSeconds);
    }

    /// <summary>
    /// Kiểm tra mã mới nhất của (người dùng, mục đích). Đúng → đánh dấu đã dùng (người gọi SaveChanges
    /// cùng thay đổi nghiệp vụ). Sai → tăng số lần sai, lưu ngay và ném 400.
    /// </summary>
    public async Task VerifyAsync(int userId, OtpPurpose purpose, string? code, decimal? amount = null)
    {
        code = code?.Trim();
        if (string.IsNullOrEmpty(code) || code.Length != 6 || !code.All(char.IsAsciiDigit))
            throw ApiException.BadRequest("Mã OTP gồm 6 chữ số.");

        var now = DateTime.UtcNow;
        var otp = await _db.OtpCodes
            .Where(o => o.UserId == userId && o.Purpose == purpose && o.UsedAt == null)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

        if (otp is null || otp.ExpiresAt <= now)
            throw ApiException.BadRequest("Mã OTP đã hết hạn hoặc chưa được gửi. Vui lòng bấm \"Gửi lại mã\".");
        if (otp.Attempts >= _opt.MaxAttempts)
            throw ApiException.BadRequest("Bạn đã nhập sai quá số lần cho phép. Vui lòng bấm \"Gửi lại mã\" để nhận mã mới.");

        var expected = Convert.FromHexString(otp.CodeHash);
        var actual = Convert.FromHexString(Hash(userId, purpose, code));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            otp.Attempts++;
            await _db.SaveChangesAsync();
            var left = _opt.MaxAttempts - otp.Attempts;
            throw ApiException.BadRequest(left > 0
                ? $"Mã OTP không đúng. Bạn còn {left} lần thử."
                : "Mã OTP không đúng. Bạn đã hết lượt thử, vui lòng bấm \"Gửi lại mã\".");
        }

        if (otp.Amount is not null && otp.Amount != amount)
            throw ApiException.BadRequest(
                $"Mã OTP này dùng để xác nhận rút {MoneyFormat.Vnd(otp.Amount.Value)}. Vui lòng gửi mã mới cho số tiền khác.");

        otp.UsedAt = now;
    }

    /// <summary>minhanh@demo.vn → mi*****@demo.vn</summary>
    public static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0) return email;
        var name = email[..at];
        var keep = name.Length <= 2 ? 1 : 2;
        return name[..keep] + new string('*', Math.Max(3, name.Length - keep)) + email[at..];
    }

    // Băm kèm userId + mục đích: cùng 6 số nhưng khác người/mục đích cho ra chuỗi băm khác nhau
    private static string Hash(int userId, OtpPurpose purpose, string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"HomeService-OTP|{userId}|{(int)purpose}|{code}")));
}

/// <summary>Nội dung email OTP (tiếng Việt, HTML đơn giản tương thích Gmail/Outlook).</summary>
public static class OtpEmail
{
    public static (string Subject, string Html, string Text) Build(string fullName, string code, OtpPurpose purpose,
        int expiryMinutes, decimal? amount = null)
    {
        var (subject, intro) = purpose switch
        {
            OtpPurpose.VerifyEmail => ("Mã xác thực đăng ký tài khoản HomeService",
                "Cảm ơn bạn đã đăng ký HomeService. Nhập mã dưới đây để kích hoạt tài khoản:"),
            OtpPurpose.ResetPassword => ("Mã đặt lại mật khẩu HomeService",
                "Bạn vừa yêu cầu đặt lại mật khẩu. Nhập mã dưới đây để tạo mật khẩu mới:"),
            OtpPurpose.ChangePassword => ("Mã xác nhận đổi mật khẩu HomeService",
                "Bạn vừa yêu cầu đổi mật khẩu khi đang đăng nhập. Nhập mã dưới đây để xác nhận:"),
            OtpPurpose.Withdraw => ("Mã xác nhận rút tiền ví HomeService",
                $"Bạn vừa yêu cầu rút {MoneyFormat.Vnd(amount ?? 0)} từ ví Tasker. Nhập mã dưới đây để xác nhận lệnh rút:"),
            OtpPurpose.AccountAppeal => ("Mã xác thực gửi yêu cầu mở khóa tài khoản",
                "Bạn vừa yêu cầu gửi đơn mở khóa tài khoản HomeService. Nhập mã dưới đây để tiếp tục:"),
            _ => ("Mã xác thực HomeService", "Mã xác thực của bạn:"),
        };
        subject = $"[HomeService] {subject}";
        var name = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(fullName) ? "bạn" : fullName.Trim());
        const string warn = "Không chia sẻ mã này cho bất kỳ ai, kể cả nhân viên HomeService. "
                            + "Nếu bạn không thực hiện yêu cầu này, hãy bỏ qua email và đổi mật khẩu.";

        var html = $"""
<!doctype html>
<html lang="vi"><body style="margin:0;padding:24px;background:#f4f8f7;font-family:Arial,Helvetica,sans-serif;color:#15302f">
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:520px;margin:0 auto;background:#ffffff;border-radius:16px;border:1px solid #e3ebe9">
    <tr><td style="padding:22px 28px;background:#13b5a5;border-radius:16px 16px 0 0;color:#ffffff;font-size:20px;font-weight:bold">HomeService</td></tr>
    <tr><td style="padding:28px">
      <p style="margin:0 0 12px;font-size:15px">Xin chào <b>{name}</b>,</p>
      <p style="margin:0 0 20px;font-size:15px;line-height:1.5">{WebUtility.HtmlEncode(intro)}</p>
      <div style="text-align:center;margin:0 0 20px">
        <span style="display:inline-block;padding:14px 26px;background:#e3f6f3;border-radius:12px;font-size:32px;font-weight:bold;letter-spacing:8px;color:#0b8b7f">{code}</span>
      </div>
      <p style="margin:0 0 8px;font-size:14px">Mã có hiệu lực trong <b>{expiryMinutes} phút</b> và chỉ dùng được 1 lần.</p>
      <p style="margin:0;font-size:13px;color:#55706d;line-height:1.5">{warn}</p>
    </td></tr>
    <tr><td style="padding:14px 28px;border-top:1px solid #e3ebe9;font-size:12px;color:#8fa3a0">Email tự động từ hệ thống HomeService – vui lòng không trả lời.</td></tr>
  </table>
</body></html>
""";
        var text = $"Xin chào {fullName},\n\n{intro}\n\n    {code}\n\nMã có hiệu lực trong {expiryMinutes} phút và chỉ dùng được 1 lần.\n{warn}\n\nHomeService";
        return (subject, html, text);
    }
}
