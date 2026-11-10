using System.Net;
using HomeService.Api.Common;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using HomeService.Api.Options;
using Microsoft.Extensions.Options;

namespace HomeService.Api.Services;

/// <summary>Một email đã dựng sẵn (tiêu đề + HTML + chữ thuần).</summary>
public record EmailContent(string Subject, string Html, string Text);

/// <summary>
/// US30 / US48 – email thông báo khóa / mở khóa tài khoản và kết quả yêu cầu mở khóa.
/// Khung email cố định; lý do & ghi chú do Admin nhập được chèn vào. Gửi lỗi chỉ ghi log, không làm hỏng thao tác chính.
/// </summary>
public class AccountEmailService
{
    private readonly IEmailSender _email;
    private readonly AccountOptions _opt;
    private readonly ILogger<AccountEmailService> _logger;

    public AccountEmailService(IEmailSender email, IOptions<AccountOptions> options, ILogger<AccountEmailService> logger)
    {
        _email = email;
        _opt = options.Value;
        _logger = logger;
    }

    public string AppealUrl(string email) =>
        $"{_opt.WebBaseUrl.TrimEnd('/')}/mo-khoa?email={Uri.EscapeDataString(email)}";

    /// <summary>Khóa có hiệu lực ngay (hoặc khi các đơn đang dở đã kết thúc).</summary>
    public EmailContent Locked(User u, AccountLockReason reason, string detail, DateTime lockedAt, IReadOnlyList<string>? orderNotes = null)
    {
        var lines = new List<Line>
        {
            new($"Tài khoản <b>{Enc(u.Email)}</b> đã bị khóa lúc <b>{VnTime.Format(lockedAt)}</b>.",
                $"Tài khoản {u.Email} đã bị khóa lúc {VnTime.Format(lockedAt)}."),
            ReasonLine(reason, detail),
        };
        if (orderNotes is { Count: > 0 })
        {
            lines.Add(new("<b>Các đơn đang dở đã được xử lý như sau:</b>", "Các đơn đang dở đã được xử lý như sau:"));
            lines.AddRange(orderNotes.Select(n => new Line($"• {Enc(n)}", $"- {n}")));
        }
        lines.Add(new("Trong thời gian bị khóa, bạn không đăng nhập và sử dụng được ứng dụng HomeService.",
            "Trong thời gian bị khóa, bạn không đăng nhập và sử dụng được ứng dụng HomeService."));
        lines.Add(AppealLine(u.Email));
        return Build("Tài khoản của bạn đã bị khóa", u.FullName, lines);
    }

    /// <summary>Admin chọn "khóa sau khi hoàn tất đơn" – báo trước cho người dùng.</summary>
    public EmailContent LockScheduled(User u, AccountLockReason reason, string detail, int activeOrders, bool isTasker)
    {
        var limit = isTasker
            ? "Từ bây giờ bạn không nhận thêm đơn mới, nhưng vẫn đăng nhập để hoàn thành các đơn đã nhận."
            : "Từ bây giờ bạn không đặt thêm đơn mới, các đơn đang có vẫn được thực hiện bình thường.";
        var lines = new List<Line>
        {
            new($"Tài khoản <b>{Enc(u.Email)}</b> sẽ bị khóa sau khi <b>{activeOrders} đơn</b> đang dở của bạn kết thúc.",
                $"Tài khoản {u.Email} sẽ bị khóa sau khi {activeOrders} đơn đang dở của bạn kết thúc."),
            ReasonLine(reason, detail),
            new(limit, limit),
            new("Khi khóa có hiệu lực, bạn sẽ nhận thêm một email kèm hướng dẫn gửi yêu cầu mở khóa.",
                "Khi khóa có hiệu lực, bạn sẽ nhận thêm một email kèm hướng dẫn gửi yêu cầu mở khóa."),
        };
        return Build("Tài khoản của bạn sắp bị khóa", u.FullName, lines);
    }

    /// <summary>Mở khóa (Admin mở trực tiếp, chấp nhận yêu cầu mở khóa, hoặc hủy lệnh khóa đang chờ).</summary>
    public EmailContent Unlocked(User u, string? note, bool viaAppeal, bool wasPending)
    {
        var head = wasPending
            ? "Lệnh khóa tài khoản đang chờ của bạn đã được hủy. Bạn có thể sử dụng HomeService bình thường."
            : viaAppeal
                ? "Yêu cầu mở khóa của bạn đã được <b>chấp nhận</b>. Tài khoản đã được mở khóa, bạn có thể đăng nhập lại."
                : "Tài khoản của bạn đã được <b>mở khóa</b>. Bạn có thể đăng nhập lại.";
        var lines = new List<Line> { new(head, StripTags(head)) };
        if (!string.IsNullOrWhiteSpace(note))
            lines.Add(new($"<b>Ghi chú của quản trị viên:</b> {Enc(note.Trim())}", $"Ghi chú của quản trị viên: {note.Trim()}"));
        lines.Add(new("Vui lòng tuân thủ quy định sử dụng để tránh bị khóa lại.", "Vui lòng tuân thủ quy định sử dụng để tránh bị khóa lại."));
        var subject = wasPending ? "Đã hủy lệnh khóa tài khoản" : viaAppeal ? "Yêu cầu mở khóa đã được chấp nhận" : "Tài khoản đã được mở khóa";
        return Build(subject, u.FullName, lines);
    }

    public EmailContent AppealReceived(User u, DateTime createdAt, int attemptNo)
    {
        var lines = new List<Line>
        {
            new($"HomeService đã nhận yêu cầu mở khóa (lần {attemptNo}/{_opt.AppealMaxPerLock}) của tài khoản <b>{Enc(u.Email)}</b> lúc {VnTime.Format(createdAt)}.",
                $"HomeService đã nhận yêu cầu mở khóa (lần {attemptNo}/{_opt.AppealMaxPerLock}) của tài khoản {u.Email} lúc {VnTime.Format(createdAt)}."),
            new("Quản trị viên sẽ xem xét và trả lời qua email trong 1–3 ngày làm việc.",
                "Quản trị viên sẽ xem xét và trả lời qua email trong 1–3 ngày làm việc."),
        };
        return Build("Đã nhận yêu cầu mở khóa tài khoản", u.FullName, lines);
    }

    public EmailContent AppealRejected(User u, string reason, DateTime? nextAppealAt, int remaining)
    {
        var lines = new List<Line>
        {
            new("Yêu cầu mở khóa tài khoản của bạn <b>chưa được chấp nhận</b>.", "Yêu cầu mở khóa tài khoản của bạn chưa được chấp nhận."),
            new($"<b>Lý do:</b> {Enc(reason.Trim())}", $"Lý do: {reason.Trim()}"),
        };
        if (remaining > 0 && nextAppealAt is not null)
        {
            lines.Add(new($"Bạn còn <b>{remaining}</b> lần gửi yêu cầu, có thể gửi lại từ <b>{VnTime.Format(nextAppealAt.Value)}</b>.",
                $"Bạn còn {remaining} lần gửi yêu cầu, có thể gửi lại từ {VnTime.Format(nextAppealAt.Value)}."));
            lines.Add(new($"Gửi lại tại: <a href=\"{Enc(AppealUrl(u.Email))}\">{Enc(AppealUrl(u.Email))}</a>",
                $"Gửi lại tại: {AppealUrl(u.Email)}"));
        }
        else
        {
            lines.Add(new("Bạn đã dùng hết số lần gửi yêu cầu mở khóa cho lần khóa này.",
                "Bạn đã dùng hết số lần gửi yêu cầu mở khóa cho lần khóa này."));
        }
        return Build("Yêu cầu mở khóa chưa được chấp nhận", u.FullName, lines);
    }

    /// <summary>Gửi email; lỗi chỉ ghi log (thao tác khóa / mở khóa vẫn thành công).</summary>
    public async Task SendSafeAsync(User u, EmailContent mail)
    {
        try
        {
            await _email.SendAsync(u.Email, u.FullName, mail.Subject, mail.Html, mail.Text);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gửi email \"{Subject}\" tới {Email} thất bại", mail.Subject, u.Email);
        }
    }

    private Line ReasonLine(AccountLockReason reason, string detail) =>
        new($"<b>Lý do:</b> {Enc(AccountLockReasonText.Of(reason))}. <i>{Enc(detail.Trim())}</i>",
            $"Lý do: {AccountLockReasonText.Of(reason)}. {detail.Trim()}");

    private Line AppealLine(string email)
    {
        var url = AppealUrl(email);
        var rule = $"Bạn được gửi tối đa {_opt.AppealMaxPerLock} yêu cầu cho mỗi lần bị khóa; mỗi yêu cầu được xử lý trong 1–3 ngày làm việc.";
        return new($"Nếu bạn cho rằng đây là nhầm lẫn, hãy gửi yêu cầu mở khóa tại: <a href=\"{Enc(url)}\">{Enc(url)}</a><br>{Enc(rule)}",
            $"Nếu bạn cho rằng đây là nhầm lẫn, hãy gửi yêu cầu mở khóa tại: {url}\n{rule}");
    }

    private sealed record Line(string Html, string Text);

    private static string Enc(string s) => WebUtility.HtmlEncode(s);

    private static string StripTags(string html) => System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", string.Empty);

    private static EmailContent Build(string title, string fullName, IReadOnlyList<Line> lines)
    {
        var subject = $"[HomeService] {title}";
        var name = string.IsNullOrWhiteSpace(fullName) ? "bạn" : fullName.Trim();
        var body = string.Join("\n", lines.Select(l =>
            $"      <p style=\"margin:0 0 12px;font-size:15px;line-height:1.55\">{l.Html}</p>"));
        var html = $"""
<!doctype html>
<html lang="vi"><body style="margin:0;padding:24px;background:#f4f8f7;font-family:Arial,Helvetica,sans-serif;color:#15302f">
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;margin:0 auto;background:#ffffff;border-radius:16px;border:1px solid #e3ebe9">
    <tr><td style="padding:22px 28px;background:#13b5a5;border-radius:16px 16px 0 0;color:#ffffff;font-size:20px;font-weight:bold">HomeService</td></tr>
    <tr><td style="padding:28px">
      <p style="margin:0 0 14px;font-size:15px">Xin chào <b>{Enc(name)}</b>,</p>
{body}
    </td></tr>
    <tr><td style="padding:14px 28px;border-top:1px solid #e3ebe9;font-size:12px;color:#8fa3a0">Email tự động từ hệ thống HomeService – vui lòng không trả lời.</td></tr>
  </table>
</body></html>
""";
        var text = $"Xin chào {name},\n\n{string.Join("\n\n", lines.Select(l => l.Text))}\n\nHomeService";
        return new EmailContent(subject, html, text);
    }
}
