namespace Payments.Service.Configuration;

public sealed class RabbitMqOptions
{
    public string Host { get; set; } = "localhost";
    public ushort Port { get; set; } = 5672;
    public string VirtualHost { get; set; } = "payments";
    public string Username { get; set; } = "payments";
    public string Password { get; set; } = string.Empty;
}
