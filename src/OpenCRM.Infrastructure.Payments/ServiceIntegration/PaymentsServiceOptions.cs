namespace OpenCRM.Infrastructure.Payments.ServiceIntegration;

/// <summary> Отдельное подключение OpenCRM к платёжному сервису; по умолчанию отключено. </summary>
public sealed class PaymentsServiceOptions
{
    public bool Enabled { get; set; }
    public bool AcceptNewPurchases { get; set; } = true;
    public bool ExpectedIsTest { get; set; } = true;
    public string MerchantAccountId { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string InternalHttpHost { get; set; } = string.Empty;
    public string ServiceKey { get; set; } = string.Empty;
    public string ReturnUrl { get; set; } = string.Empty;
    public string[] AllowedReturnUrls { get; set; } = [];
    public string Host { get; set; } = string.Empty;
    public ushort Port { get; set; } = 5672;
    public string VirtualHost { get; set; } = "payments";
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int PollSeconds { get; set; } = 5;
    public int SendTimeoutSeconds { get; set; } = 15;
    public string[] AllowedConfirmationHosts { get; set; } = ["yoomoney.ru", "yookassa.ru"];

    public static bool IsValid(PaymentsServiceOptions options)
    {
        if (!options.Enabled)
        {
            return true;
        }

        return options.MerchantAccountId.Length is >= 1 and <= 128
            && Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var service)
            && (service.Scheme == Uri.UriSchemeHttps
                || service.Scheme == Uri.UriSchemeHttp
                    && (service.IsLoopback
                        || Uri.CheckHostName(options.InternalHttpHost) == UriHostNameType.Dns
                            && string.Equals(service.Host, options.InternalHttpHost, StringComparison.OrdinalIgnoreCase)))
            && string.IsNullOrEmpty(service.UserInfo) && string.IsNullOrEmpty(service.Query)
            && string.IsNullOrEmpty(service.Fragment)
            && options.ServiceKey.Length >= 32
            && Uri.TryCreate(options.ReturnUrl, UriKind.Absolute, out var result)
            && result.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(result.UserInfo)
            && string.IsNullOrEmpty(result.Query) && string.IsNullOrEmpty(result.Fragment)
            && !string.IsNullOrWhiteSpace(options.Host) && options.Port > 0
            && !string.IsNullOrWhiteSpace(options.VirtualHost) && options.VirtualHost != "/"
            && !string.IsNullOrWhiteSpace(options.Username) && !string.IsNullOrWhiteSpace(options.Password)
            && options.PollSeconds is >= 1 and <= 60 && options.SendTimeoutSeconds is >= 1 and <= 30
            && options.AllowedConfirmationHosts.Length > 0
            && options.AllowedConfirmationHosts.All(host => Uri.CheckHostName(host) == UriHostNameType.Dns)
            && options.AllowedReturnUrls.Length > 0
            && options.AllowedReturnUrls.All(value => Uri.TryCreate(value, UriKind.Absolute, out var allowed)
                && allowed.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(allowed.UserInfo)
                && string.IsNullOrEmpty(allowed.Query) && string.IsNullOrEmpty(allowed.Fragment));
    }
}
