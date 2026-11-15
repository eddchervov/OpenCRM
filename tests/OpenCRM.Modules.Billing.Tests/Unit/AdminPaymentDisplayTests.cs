using Helpers.Payments;

namespace OpenCRM.Modules.Billing.Tests.Unit;

public sealed class AdminPaymentDisplayTests
{
    [Theory]
    [InlineData("completed", "success")]
    [InlineData("succeeded", "success")]
    [InlineData("success", "success")]
    [InlineData("paid", "success")]
    [InlineData("done", "success")]
    [InlineData(" PENDING ", "pending")]
    [InlineData("processing", "pending")]
    [InlineData("waiting_for_capture", "pending")]
    [InlineData("paid_waiting_confirmation", "pending")]
    [InlineData("cancelled_by_provider", "canceled")]
    [InlineData("partial_refund", "refunded")]
    [InlineData("failure", "problem")]
    [InlineData("gateway_error", "problem")]
    [InlineData(null, "unknown")]
    [InlineData("  ", "unknown")]
    [InlineData("chargeback", "other")]
    public void ResolvePaymentStatusGroupClassifiesKnownAndUnknownStatuses(string? status, string expectedGroup)
    {
        Assert.Equal(expectedGroup, AdminPaymentDisplay.ResolvePaymentStatusGroup(status));
    }

    [Theory]
    [InlineData("success", "Успешные")]
    [InlineData("pending", "Ожидают")]
    [InlineData("problem", "Ошибки")]
    [InlineData("canceled", "Отменены")]
    [InlineData("refunded", "Возвраты")]
    [InlineData("unknown", "Без статуса")]
    [InlineData("other", "Другие")]
    public void ResolvePaymentStatusGroupNameReturnsRussianLabels(string group, string expectedName)
    {
        Assert.Equal(expectedName, AdminPaymentDisplay.ResolvePaymentStatusGroupName(group));
    }

    [Theory]
    [InlineData("completed", "Оплачено")]
    [InlineData("succeeded", "Успешно в YooKassa")]
    [InlineData("pending", "Ожидание")]
    [InlineData("processing", "Обработка")]
    [InlineData("waiting_for_capture", "Ждет подтверждения")]
    [InlineData("paid_waiting_confirmation", "Оплачен, ждет подтверждения")]
    [InlineData("canceled", "Отменено")]
    [InlineData("failed", "Ошибка")]
    [InlineData("refunded", "Возврат")]
    [InlineData(null, "Без статуса")]
    [InlineData("  ", "Без статуса")]
    [InlineData("custom_status", "custom_status")]
    public void ResolvePaymentStatusNamePreservesKnownLabelsAndUnknownValues(string? status, string expectedName)
    {
        Assert.Equal(expectedName, AdminPaymentDisplay.ResolvePaymentStatusName(status));
    }

    [Theory]
    [InlineData("yookassa", "YooKassa")]
    [InlineData("yookassa_purchase", "YooKassa: покупка")]
    [InlineData("yookassa_extension", "YooKassa: продление")]
    [InlineData("yookassa_workspace_purchase:abc", "YooKassa: покупка организации")]
    [InlineData("yookassa_workspace_extension:abc", "YooKassa: продление организации")]
    [InlineData("workspace_purchase:abc", "Покупка организации")]
    [InlineData("workspace_extension:abc", "Продление организации")]
    [InlineData(null, "Не указан")]
    [InlineData("   ", "Не указан")]
    [InlineData(" custom-method ", "custom-method")]
    public void ResolvePaymentMethodNameReturnsFriendlyNamesAndPreservesUnknownMethods(string? method, string expectedName)
    {
        Assert.Equal(expectedName, AdminPaymentDisplay.ResolvePaymentMethodName(method));
    }
}
