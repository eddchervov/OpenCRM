using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCRM.Services.Interfaces.Subscriptions.Dtos
{
    /// <summary> DTO платежа по подписке. </summary>
    public class SubscriptionPaymentDto
    {
        /// <summary> Идентификатор платежа. </summary>
        public int Id { get; set; }

        /// <summary> Сумма платежа. </summary>
        public decimal Amount { get; set; }

        /// <summary> Валюта платежа. </summary>
        public string Currency { get; set; } = "RUB";

        /// <summary> Дата платежа. </summary>
        public DateTime PaymentDate { get; set; }

        /// <summary> Способ платежа. </summary>
        public string PaymentMethod { get; set; } = string.Empty;

        /// <summary> Статус платежа. </summary>
        public string PaymentStatus { get; set; } = string.Empty;

        /// <summary> Идентификатор транзакции, если есть. </summary>
        public string? TransactionId { get; set; }

        /// <summary> Тарифный план платежа. </summary>
        public SubscriptionPlanDto SubscriptionPlan { get; set; } = new SubscriptionPlanDto();

        /// <summary> Текстовое описание статуса платежа. </summary>
        public string PaymentStatusText => PaymentStatus switch
        {
            "completed" => "Оплачено",
            "pending" => "Ожидание",
            "processing" => "Обработка",
            "waiting_for_capture" => "Подтверждение платежа",
            "canceled" => "Отменено",
            "failed" => "Ошибка",
            "refunded" => "Возврат",
            _ => PaymentStatus
        };
    }
}

