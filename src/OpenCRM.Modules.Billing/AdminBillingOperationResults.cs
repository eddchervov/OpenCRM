using OpenCRM.Modules.Billing.Contracts.Dtos;

namespace OpenCRM.Modules.Billing;

/// <summary> Хелперы формирования результатов административных операций биллинга. </summary>
internal static class AdminBillingOperationResults
{
    /// <summary> Формирует результат «платёж не найден». </summary>
    public static AdminBillingPaymentOperationDto PaymentMissing(string message)
    {
        return new AdminBillingPaymentOperationDto
        {
            NotFound = true,
            ErrorMessage = message
        };
    }

    /// <summary> Формирует результат «некорректный платёж». </summary>
    public static AdminBillingPaymentOperationDto PaymentInvalid(string message)
    {
        return new AdminBillingPaymentOperationDto
        {
            ErrorMessage = message
        };
    }

    /// <summary> Формирует результат «подписка не найдена». </summary>
    public static AdminBillingSubscriptionOperationDto SubscriptionMissing(string message)
    {
        return new AdminBillingSubscriptionOperationDto
        {
            NotFound = true,
            ErrorMessage = message
        };
    }

    /// <summary> Формирует результат «некорректная подписка». </summary>
    public static AdminBillingSubscriptionOperationDto SubscriptionInvalid(string message)
    {
        return new AdminBillingSubscriptionOperationDto
        {
            ErrorMessage = message
        };
    }
}
