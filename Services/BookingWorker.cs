using HomeService.Api.Options;
using Microsoft.Extensions.Options;

namespace HomeService.Api.Services;

/// <summary>
/// BackgroundService chạy định kỳ (Matching:TickSeconds, mặc định 5 giây) – không chặn request tạo đơn.
/// Mỗi lượt chạy các tác vụ tự động của hệ thống theo thứ tự.
/// </summary>
public class BookingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BookingWorker> _logger;
    private readonly MatchingOptions _matching;

    public BookingWorker(IServiceScopeFactory scopeFactory, ILogger<BookingWorker> logger, IOptions<MatchingOptions> matching)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _matching = matching.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _matching.TickSeconds)));
        do
        {
            await RunStepAsync<BookingJobs>(s => s.ExpireUnpaidBookingsAsync(stoppingToken), "hủy đơn quá hạn thanh toán", stoppingToken);
            await RunStepAsync<MatchingService>(s => s.ProcessDueBookingsAsync(stoppingToken), "ghép nối", stoppingToken);
            await RunStepAsync<BookingJobs>(s => s.AutoConfirmAsync(stoppingToken), "tự xác nhận hoàn thành", stoppingToken);
            await RunStepAsync<BookingJobs>(s => s.ExpireNoTaskerFoundAsync(stoppingToken), "hết hạn đơn chưa có Tasker", stoppingToken);
            await RunStepAsync<BookingJobs>(s => s.DetectNoShowAsync(stoppingToken), "phát hiện Tasker không đến", stoppingToken);
            await RunStepAsync<BookingJobs>(s => s.SendAppointmentRemindersAsync(stoppingToken), "nhắc lịch hẹn", stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunStepAsync<TService>(Func<TService, Task> step, string name, CancellationToken ct)
        where TService : notnull
    {
        if (ct.IsCancellationRequested) return;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            await step(scope.ServiceProvider.GetRequiredService<TService>());
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Tác vụ nền '{Name}' lỗi", name);
        }
    }
}
