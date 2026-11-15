namespace OpenCRM.Modules.Payments.Contracts;

/// <summary>Создаёт оплату либо возобновляет сохранённую операцию без пересчёта её суммы.</summary>
public interface IPaymentCreationGateway
{
    /// <summary>Возобновляет сохранённый запрос; возвращает null, если операция ещё не существует.</summary>
    Task<PaymentCreationResult?> ResumeAsync(PaymentCreationIntent intent);

    /// <summary>Фиксирует запрос до отправки; повтор того же намерения использует прежнюю сумму и параметры.</summary>
    Task<PaymentCreationResult> CreateAsync(PaymentCreationDraft draft);
}
