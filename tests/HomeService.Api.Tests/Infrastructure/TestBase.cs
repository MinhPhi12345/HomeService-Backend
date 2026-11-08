using HomeService.Api.Data;
using HomeService.Api.Options;
using HomeService.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace HomeService.Api.Tests.Infrastructure;

/// <summary>
/// Lớp cơ sở: tạo CSDL test và "lắp" các service thật của Backend với cấu hình mặc định
/// (giống appsettings.json). Test có thể đổi Settlement/Matching/Business trước khi gọi service.
/// </summary>
public abstract class TestBase : IDisposable
{
    protected readonly TestDb TestDb = new();
    protected AppDbContext Db => TestDb.Db;
    protected AppDbContext Fresh() => TestDb.Fresh();

    protected SettlementOptions SettlementOptions { get; } = new();
    protected MatchingOptions MatchingOptions { get; } = new();
    protected BusinessOptions BusinessOptions { get; } = new();
    protected NotificationOptions NotificationOptions { get; } = new();
    protected HttpContextAccessor HttpContextAccessor { get; } = new();
    protected OtpOptions OtpOptions { get; } = new();
    protected FakeEmailSender Emails { get; } = new();
    protected AwardOptions AwardOptions { get; } = new();
    protected PromotionOptions PromotionOptions { get; } = new();

    protected NotificationService CreateNotifications() => new(Db);
    protected OtpService CreateOtp() => new(Db, Emails, MsOptions.Create(OtpOptions), NullLogger<OtpService>.Instance);
    protected WalletService CreateWallet() => new(Db);
    protected AuditService CreateAudit() => new(Db, HttpContextAccessor);
    protected VoucherService CreateVouchers() => new(Db, CreateNotifications(), MsOptions.Create(PromotionOptions));
    protected PromotionService CreatePromotions() => new(Db, CreateVouchers(), CreateAudit());
    protected TaskerAwardService CreateAwards() => new(Db, CreateWallet(), CreateNotifications(), CreateAudit(), Emails,
        MsOptions.Create(AwardOptions), MsOptions.Create(BusinessOptions), NullLogger<TaskerAwardService>.Instance);

    protected SettlementService CreateSettlement() => new(Db, CreateWallet(), CreateNotifications(), CreateAudit(),
        NullLogger<SettlementService>.Instance, MsOptions.Create(SettlementOptions), CreateVouchers());

    protected BookingService CreateBookingService() => new(Db, CreateNotifications(),
        MsOptions.Create(BusinessOptions), MsOptions.Create(SettlementOptions), CreateSettlement(), CreateWallet(),
        MsOptions.Create(MatchingOptions), CreateAudit(), CreateVouchers());

    protected PaymentService CreatePaymentService() => new(Db, CreateNotifications(), CreateAudit(), CreateWallet(), CreateVouchers());

    protected CancellationService CreateCancellation() => new(Db, CreateSettlement(), CreateWallet(), CreateNotifications(),
        CreateAudit(), MsOptions.Create(SettlementOptions), CreateVouchers());

    protected OfferService CreateOfferService() => new(Db, CreateWallet(), CreateNotifications(), MsOptions.Create(MatchingOptions));

    protected MatchingService CreateMatching() => new(Db, CreateNotifications(), MsOptions.Create(MatchingOptions),
        NullLogger<MatchingService>.Instance);

    protected TaskerDisciplineService CreateDiscipline() => new(Db, CreateNotifications(), MsOptions.Create(SettlementOptions));

    protected DisputeService CreateDisputeService() => new(Db, CreateSettlement(), CreateDiscipline(), CreateNotifications(),
        CreateAudit(), CreateBookingService(), MsOptions.Create(SettlementOptions), CreateVouchers());

    protected BookingJobs CreateJobs() => new(Db, CreateNotifications(), CreateSettlement(), MsOptions.Create(SettlementOptions),
        NullLogger<BookingJobs>.Instance, CreateCancellation(), CreateDiscipline(), CreateWallet(),
        MsOptions.Create(NotificationOptions), CreateVouchers());

    public virtual void Dispose()
    {
        TestDb.Dispose();
        GC.SuppressFinalize(this);
    }
}
