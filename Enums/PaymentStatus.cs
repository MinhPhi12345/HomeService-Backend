namespace HomeService.Api.Enums;

public enum PaymentMethod
{
    Cash = 0,
    EWallet = 1,
    Card = 2
}

public enum PaymentStatus
{
    Pending = 0,
    Success = 1,
    Failed = 2
}
