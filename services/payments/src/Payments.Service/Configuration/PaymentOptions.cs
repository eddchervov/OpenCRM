namespace Payments.Service.Configuration;

public sealed class PaymentOptions
{
    public string Mode { get; set; } = "Disabled";
    public string ApplicationId { get; set; } = "opencrm";
    public string MerchantAccountId { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string ServiceKey { get; set; } = string.Empty;
    public bool IsTest { get; set; } = true;
    public string[] AllowedReturnUrls { get; set; } = [];
    public int PollSeconds { get; set; } = 5;
    public int RetryWindowHours { get; set; } = 23;
}
