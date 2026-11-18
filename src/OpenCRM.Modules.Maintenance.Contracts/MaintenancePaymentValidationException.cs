namespace OpenCRM.Modules.Maintenance.Contracts;

/// <summary> Ошибка проверки оплаты, сообщение которой можно локализовать в HTTP-контроллере. </summary>
public sealed class MaintenancePaymentValidationException : InvalidOperationException
{
    public MaintenancePaymentValidationException(string messageKey, string message, params string[] arguments)
        : base(message)
    {
        this.MessageKey = messageKey;
        this.Arguments = Array.AsReadOnly(arguments);
    }

    /// <summary> Ключ локализованного сообщения. </summary>
    public string MessageKey { get; }

    /// <summary> Строковые аргументы локализованного сообщения. </summary>
    public IReadOnlyList<string> Arguments { get; }
}
