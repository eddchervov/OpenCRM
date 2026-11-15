using Microsoft.EntityFrameworkCore;
using OpenCRM.Infrastructure.Payments.ServiceIntegration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Promotions.Contracts;
using OpenCRM.Services.Interfaces.Payments;
using OpenCRM.Services.Interfaces.Payments.Dto;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace OpenCRM.Infrastructure.Payments;

/// <summary> Сервис взаимодействия с платёжным провайдером YooKassa. </summary>
public class YooKassaService : IYooKassaService
{
    /// <summary> Настройки сериализации JSON. </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary> HTTP-клиент для запросов к YooKassa. </summary>
    private readonly HttpClient _httpClient;

    /// <summary> Логгер сервиса. </summary>
    private readonly ILogger<YooKassaService> _logger;

    /// <summary> Конфигурация YooKassa. </summary>
    private readonly YooKassaConfig _config;

    /// <summary> Контекст базы данных. </summary>
    private readonly OpenCRMContext _context;

    /// <summary> Зависимость обработчика создания платежей. </summary>
    private readonly PaymentCreationProcessor _paymentCreationProcessor;

    private readonly bool _newPaymentsUseService;

    /// <summary> Создаёт сервис с HTTP-клиентом, конфигурацией и контекстом. </summary>
    public YooKassaService(
        HttpClient httpClient,
        ILogger<YooKassaService> logger,
        IOptions<YooKassaConfig> config,
        OpenCRMContext context,
        IPromotionPaymentCoordinator? promotionPaymentCoordinator = null,
        IOptions<PaymentsServiceOptions>? paymentsServiceOptions = null)
    {
        this._newPaymentsUseService = paymentsServiceOptions?.Value.Enabled == true;
        _httpClient = httpClient;
        _logger = logger;
        _config = config.Value;
        _context = context;
        _paymentCreationProcessor = new PaymentCreationProcessor(
            _httpClient,
            _context,
            logger,
            _config,
            promotionPaymentCoordinator);

        // Настройка HttpClient для YooKassa
        var authBytes = Encoding.UTF8.GetBytes($"{_config.ShopId}:{_config.SecretKey}");
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
    }

    /// <summary> Создаёт платёж в YooKassa с идемпотентностью. </summary>
    public async Task<PaymentResponse> CreatePaymentAsync(
        string userId,
        int planId,
        decimal amount,
        string description,
        string operationType,
        PaymentBuyerDto buyer,
        string? returnUrl = null,
        PaymentPromotionDto? promotion = null,
        string? idempotenceKey = null,
        DateTime? requestTimestamp = null)
    {
        if (this._newPaymentsUseService)
        {
            return PaymentResponse.CreateError("Новые оплаты создаются через платёжный микросервис.");
        }

        var buyerError = ValidateBuyer(buyer);
        if (buyerError != null)
        {
            return PaymentResponse.CreateError(buyerError);
        }

        var resolvedReturnUrl = string.IsNullOrWhiteSpace(returnUrl)
            ? $"{_config.ReturnUrl}/payment/complete"
            : returnUrl;

        try
        {
            return await _paymentCreationProcessor.CreatePaymentAsync(
                userId,
                planId,
                amount,
                description,
                operationType,
                buyer,
                resolvedReturnUrl,
                promotion,
                idempotenceKey,
                requestTimestamp);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error creating YooKassa payment");
            return PaymentResponse.CreateError("Ошибка при создании платежа");
        }
    }

    /// <summary> Проверяет корректность данных покупателя. </summary>
    internal static string? ValidateBuyer(PaymentBuyerDto? buyer)
    {
        if (buyer?.TypeCode == "individual")
        {
            return null;
        }

        if (buyer?.TypeCode != "business")
        {
            return "Укажите, кто покупает подписку: физическое лицо или организация/ИП.";
        }

        if (string.IsNullOrWhiteSpace(buyer.Name) || buyer.Name.Trim().Length > 200)
        {
            return "Укажите наименование организации или ФИО ИП (до 200 символов).";
        }

        var inn = buyer.Inn?.Trim();
        if (inn == null || inn.Length is not (10 or 12) || !inn.All(char.IsAsciiDigit))
        {
            return "ИНН организации или ИП должен содержать 10 или 12 цифр.";
        }

        if (!HasValidInnChecksum(inn))
        {
            return "Проверьте контрольные цифры ИНН организации или ИП.";
        }

        return null;
    }

    /// <summary> Нормализует данные покупателя. </summary>
    internal static PaymentBuyerDto NormalizeBuyer(PaymentBuyerDto buyer) =>
        new()
        {
            TypeCode = buyer.TypeCode,
            Name = buyer.TypeCode == "business" ? buyer.Name?.Trim() : null,
            Inn = buyer.TypeCode == "business" ? buyer.Inn?.Trim() : null
        };

    /// <summary> Проверяет контрольную сумму ИНН. </summary>
    private static bool HasValidInnChecksum(string inn)
    {
        static int CheckDigit(string value, int[] weights)
        {
            var sum = 0;
            for (var index = 0; index < weights.Length; index++)
            {
                sum += (value[index] - '0') * weights[index];
            }

            return sum % 11 % 10;
        }

        if (inn.Length == 10)
        {
            return CheckDigit(inn, [2, 4, 10, 3, 5, 9, 4, 6, 8]) == inn[9] - '0';
        }

        return CheckDigit(inn, [7, 2, 4, 10, 3, 5, 9, 4, 6, 8]) == inn[10] - '0'
            && CheckDigit(inn, [3, 7, 2, 4, 10, 3, 5, 9, 4, 6, 8]) == inn[11] - '0';
    }

    /// <summary> Подтверждает списание платежа в YooKassa. </summary>
    public async Task<PaymentResponse> CapturePaymentAsync(string paymentId)
    {
        if (await this.IsServicePaymentAsync(paymentId))
        {
            return PaymentResponse.CreateError("Эта оплата управляется платёжным микросервисом.");
        }

        try
        {
            var paymentAmount = await GetPaymentAmount(paymentId);

            var captureData = new
            {
                amount = new
                {
                    value = paymentAmount.ToString("F2"),
                    currency = "RUB"
                }
            };

            using var request = CreateJsonPostRequest(
                $"payments/{paymentId}/capture",
                captureData,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                return PaymentResponse.CreateSuccess(message: "Платеж подтвержден");
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("YooKassa payment capture failed: {Error}", errorContent);
                return PaymentResponse.CreateError("Ошибка подтверждения платежа");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error capturing payment {PaymentId}", paymentId);
            return PaymentResponse.CreateError("Ошибка подтверждения платежа");
        }
    }

    /// <summary> Отменяет платёж в YooKassa. </summary>
    public async Task<PaymentResponse> CancelPaymentAsync(string paymentId)
    {
        if (await this.IsServicePaymentAsync(paymentId))
        {
            return PaymentResponse.CreateError("Эта оплата управляется платёжным микросервисом.");
        }

        try
        {
            using var request = CreateEmptyPostRequest($"payments/{paymentId}/cancel");
            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                return PaymentResponse.CreateSuccess(message: "Платеж отменен");
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("YooKassa payment cancellation failed: {Error}", errorContent);
                return PaymentResponse.CreateError("Ошибка отмены платежа");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error canceling payment {PaymentId}", paymentId);
            return PaymentResponse.CreateError("Ошибка отмены платежа");
        }
    }

    /// <summary> Возвращает актуальный статус платежа из YooKassa. </summary>
    public async Task<PaymentResponse> GetPaymentStatusAsync(string paymentId)
    {
        if (await this.IsServicePaymentAsync(paymentId))
        {
            return PaymentResponse.CreateError("Эта оплата управляется платёжным микросервисом.");
        }

        try
        {
            var payment = await GetRemotePaymentAsync(paymentId);
            if (payment != null)
            {
                return new PaymentResponse
                {
                    IsSuccess = true,
                    PaymentId = payment.Id,
                    Status = payment.Status,
                    Paid = payment.Paid,
                    Amount = ParseMoney(payment.Amount.Value),
                    Currency = payment.Amount.Currency,
                    RefundedAmount = payment.RefundedAmount == null ? 0 : ParseMoney(payment.RefundedAmount.Value),
                    RefundedCurrency = payment.RefundedAmount?.Currency,
                    CreatedAt = payment.CreatedAt
                };
            }

            return PaymentResponse.CreateError("Ошибка получения статуса платежа");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting payment status {PaymentId}", paymentId);
            return PaymentResponse.CreateError("Ошибка получения статуса платежа");
        }
    }

    /// <summary> Возвращает локальную информацию о платеже. </summary>
    public async Task<LocalPaymentInfoDto?> GetLocalPaymentInfoAsync(string paymentId)
    {
        return await _context.SubscriptionPayments
            .Where(p => p.ExternalPaymentId == paymentId)
            .Select(p => new LocalPaymentInfoDto
            {
                UserId = p.UserId,
                PlanId = p.SubscriptionPlanId,
                Amount = p.Amount,
                PaymentStatus = p.PaymentStatus,
                PaymentMethod = p.PaymentMethod
            })
            .FirstOrDefaultAsync();
    }

    /// <summary> Помечает локальный платёж как оплаченный. </summary>
    public async Task<bool> MarkPaymentPaidAsync(string paymentId)
    {
        if (await this.IsServicePaymentAsync(paymentId))
        {
            return false;
        }

        var updated = await _context.SubscriptionPayments
            .Where(payment => payment.ExternalPaymentId == paymentId && payment.PaidAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(payment => payment.PaidAt, DateTime.UtcNow));

        return updated > 0 || await _context.SubscriptionPayments
            .AnyAsync(payment => payment.ExternalPaymentId == paymentId && payment.PaidAt != null);
    }

    /// <summary> Обновляет локальный статус платежа. </summary>
    public async Task<bool> UpdateLocalPaymentStatusAsync(string paymentId, string status)
    {
        if (await this.IsServicePaymentAsync(paymentId))
        {
            return false;
        }

        var payments = _context.SubscriptionPayments
            .Where(payment => payment.ExternalPaymentId == paymentId
                && payment.PaymentStatus != "completed" && payment.PaymentStatus != "refunded");
        var updated = status is "completed" or "canceled"
            ? await payments.ExecuteUpdateAsync(setters => setters
                .SetProperty(payment => payment.PaymentStatus, status)
                .SetProperty(payment => payment.StatusUpdatedAt, DateTime.UtcNow))
            : await payments.ExecuteUpdateAsync(setters => setters
                .SetProperty(payment => payment.PaymentStatus, status));

        return updated > 0 || await _context.SubscriptionPayments
            .AnyAsync(payment => payment.ExternalPaymentId == paymentId);
    }

    /// <summary> Проверяет подлинность уведомления YooKassa. </summary>
    public async Task<bool> VerifyNotification(string ipnData, string signature)
    {
        try
        {
            var notification = JsonSerializer.Deserialize<YooKassaWebhookNotification>(ipnData, JsonOptions);
            if (notification?.Object == null
                || !string.Equals(notification.Type, "notification", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(notification.Event)
                || string.IsNullOrWhiteSpace(notification.Object.Id))
            {
                _logger.LogWarning("YooKassa webhook rejected: invalid notification payload.");
                return false;
            }

            if (!IsSupportedPaymentEvent(notification.Event))
            {
                _logger.LogInformation("YooKassa webhook ignored: unsupported event {Event}.", notification.Event);
                return true;
            }

            var localPayment = await GetLocalPaymentInfoAsync(notification.Object.Id);
            if (localPayment == null)
            {
                _logger.LogWarning("YooKassa webhook rejected: local payment {PaymentId} not found.", notification.Object.Id);
                return false;
            }

            var remotePayment = await GetRemotePaymentAsync(notification.Object.Id);
            if (remotePayment == null)
            {
                _logger.LogWarning("YooKassa webhook rejected: payment {PaymentId} was not confirmed by YooKassa API.", notification.Object.Id);
                return false;
            }

            if (!string.Equals(remotePayment.Id, notification.Object.Id, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("YooKassa webhook rejected: remote payment id mismatch for {PaymentId}.", notification.Object.Id);
                return false;
            }

            if (!IsExpectedRemoteStatus(notification.Event, remotePayment.Status))
            {
                _logger.LogWarning(
                    "YooKassa webhook rejected: event {Event} does not match remote status {Status} for payment {PaymentId}.",
                    notification.Event,
                    remotePayment.Status,
                    notification.Object.Id);
                return false;
            }

            if (RequiresPaidPayment(notification.Event) && !remotePayment.Paid)
            {
                _logger.LogWarning("YooKassa webhook rejected: payment {PaymentId} is not paid.", notification.Object.Id);
                return false;
            }

            if (!string.Equals(remotePayment.Amount.Currency, "RUB", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "YooKassa webhook rejected: payment {PaymentId} has unexpected currency {Currency}.",
                    notification.Object.Id,
                    remotePayment.Amount.Currency);
                return false;
            }

            var remoteAmount = ParseMoney(remotePayment.Amount.Value);
            if (remoteAmount != localPayment.Amount)
            {
                _logger.LogWarning(
                    "YooKassa webhook rejected: payment {PaymentId} amount mismatch. Local {LocalAmount}, remote {RemoteAmount}.",
                    notification.Object.Id,
                    localPayment.Amount,
                    remoteAmount);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "YooKassa webhook rejected: notification verification failed.");
            return false;
        }
    }

    /// <summary> Получает статус платежа из YooKassa. </summary>
    private async Task<YooKassaPaymentStatusResponse?> GetRemotePaymentAsync(string paymentId)
    {
        var response = await _httpClient.GetAsync($"payments/{paymentId}");
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("YooKassa payment status check failed: {Error}", errorContent);
            return null;
        }

        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<YooKassaPaymentStatusResponse>(content, JsonOptions);
    }

    /// <summary> Проверяет поддержку события уведомления. </summary>
    private static bool IsSupportedPaymentEvent(string eventName)
    {
        return eventName is "payment.waiting_for_capture" or "payment.succeeded" or "payment.canceled";
    }

    /// <summary> Проверяет соответствие удалённого статуса событию. </summary>
    private static bool IsExpectedRemoteStatus(string eventName, string remoteStatus)
    {
        return eventName switch
        {
            "payment.waiting_for_capture" => string.Equals(remoteStatus, "waiting_for_capture", StringComparison.OrdinalIgnoreCase),
            "payment.succeeded" => string.Equals(remoteStatus, "succeeded", StringComparison.OrdinalIgnoreCase),
            "payment.canceled" => string.Equals(remoteStatus, "canceled", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    /// <summary> Определяет, требует ли событие оплаты платежа. </summary>
    private static bool RequiresPaidPayment(string eventName)
    {
        return eventName is "payment.waiting_for_capture" or "payment.succeeded";
    }

    /// <summary> Разбирает денежную сумму из строки. </summary>
    private static decimal ParseMoney(string value)
    {
        return decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
    }

    /// <summary> Возвращает сумму платежа. </summary>
    private async Task<decimal> GetPaymentAmount(string paymentId)
    {
        var payment = await _context.SubscriptionPayments
            .FirstOrDefaultAsync(p => p.ExternalPaymentId == paymentId);
        return payment?.Amount ?? 0;
    }

    /// <summary> Формирует POST-запрос с JSON-телом. </summary>
    private HttpRequestMessage CreateJsonPostRequest(
        string uri, object payload, JsonSerializerOptions options, string? idempotenceKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, options),
                Encoding.UTF8,
                "application/json")
        };

        request.Headers.Add("Idempotence-Key", idempotenceKey ?? Guid.NewGuid().ToString());
        return request;
    }

    /// <summary> Формирует POST-запрос без тела. </summary>
    private HttpRequestMessage CreateEmptyPostRequest(string uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.Add("Idempotence-Key", Guid.NewGuid().ToString());
        return request;
    }

    /// <summary> Модель уведомления вебхука YooKassa. </summary>
    private sealed class YooKassaWebhookNotification
    {
        /// <summary> Тип уведомления. </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        /// <summary> Событие уведомления. </summary>
        [JsonPropertyName("event")]
        public string Event { get; set; } = string.Empty;

        /// <summary> Данные платежа из уведомления. </summary>
        [JsonPropertyName("object")]
        public YooKassaWebhookPayment Object { get; set; } = null!;
    }

    /// <summary> Модель платежа из вебхука YooKassa. </summary>
    private sealed class YooKassaWebhookPayment
    {
        /// <summary> Идентификатор платежа. </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
    }
    private Task<bool> IsServicePaymentAsync(string paymentId) => this._context.SubscriptionPayments
        .AnyAsync(payment => payment.ExternalPaymentId == paymentId && payment.PaymentProviderCode == "payments-service");
}
