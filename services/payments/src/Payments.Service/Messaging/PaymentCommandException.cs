namespace Payments.Service.Messaging;

public sealed class PaymentCommandException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
