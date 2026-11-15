namespace Helpers.Payments;

/// <summary>Сохраняет единое отображение статусов и способов оплаты в административном биллинге.</summary>
public static class AdminPaymentDisplay
{
    /// <summary> Определяет группу статуса платежа для административного интерфейса. </summary>
    public static string ResolvePaymentStatusGroup(string? status)
    {
        var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();

        if (normalized is "completed" or "succeeded" or "success" or "paid" or "done")
        {
            return "success";
        }

        if (normalized is "pending" or "processing" or "waiting_for_capture" or "paid_waiting_confirmation")
        {
            return "pending";
        }

        if (normalized.Contains("cancel"))
        {
            return "canceled";
        }

        if (normalized.Contains("refund"))
        {
            return "refunded";
        }

        if (normalized.Contains("fail") || normalized.Contains("error"))
        {
            return "problem";
        }

        return string.IsNullOrWhiteSpace(normalized) ? "unknown" : "other";
    }

    /// <summary> Возвращает отображаемое название группы статуса платежа. </summary>
    public static string ResolvePaymentStatusGroupName(string group) => group switch
    {
        "success" => "Успешные",
        "pending" => "Ожидают",
        "problem" => "Ошибки",
        "canceled" => "Отменены",
        "refunded" => "Возвраты",
        "unknown" => "Без статуса",
        _ => "Другие"
    };

    /// <summary> Возвращает отображаемое название статуса платежа. </summary>
    public static string ResolvePaymentStatusName(string? status) => (status ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "completed" => "Оплачено",
        "succeeded" => "Успешно в YooKassa",
        "pending" => "Ожидание",
        "processing" => "Обработка",
        "waiting_for_capture" => "Ждет подтверждения",
        "paid_waiting_confirmation" => "Оплачен, ждет подтверждения",
        "canceled" => "Отменено",
        "failed" => "Ошибка",
        "refunded" => "Возврат",
        "" => "Без статуса",
        var value => value
    };

    /// <summary> Возвращает отображаемое название способа оплаты. </summary>
    public static string ResolvePaymentMethodName(string? method) => (method ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "yookassa" => "YooKassa",
        "yookassa_purchase" => "YooKassa: покупка",
        "yookassa_extension" => "YooKassa: продление",
        var value when value.StartsWith("yookassa_workspace_purchase:") => "YooKassa: покупка организации",
        var value when value.StartsWith("yookassa_workspace_extension:") => "YooKassa: продление организации",
        var value when value.StartsWith("workspace_purchase:") => "Покупка организации",
        var value when value.StartsWith("workspace_extension:") => "Продление организации",
        "" => "Не указан",
        var value => value
    };
}
