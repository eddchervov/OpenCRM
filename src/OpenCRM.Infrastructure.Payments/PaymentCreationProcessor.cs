using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Promotions.Contracts;
using OpenCRM.Services.Interfaces.Payments.Dto;

namespace OpenCRM.Infrastructure.Payments;

/// <summary> Обработчик создания платежей через платёжного провайдера. </summary>
internal sealed class PaymentCreationProcessor
{
    /// <summary> HTTP-клиент для запросов к провайдеру. </summary>
    private readonly HttpClient _httpClient;

    /// <summary> Контекст базы данных. </summary>
    private readonly OpenCRMContext _context;

    /// <summary> Логгер обработчика. </summary>
    private readonly ILogger _logger;

    /// <summary> Зависимость координатора промо-платежей. </summary>
    private readonly IPromotionPaymentCoordinator? _promotionPaymentCoordinator;

    /// <summary> Статус требования ручного разбора. </summary>
    private const string ManualReviewStatus = PaymentCreationOperationStatuses.ManualReview;

    /// <summary> Статус подготовленной операции. </summary>
    private const string PreparedStatus = PaymentCreationOperationStatuses.Prepared;

    /// <summary> Создаёт обработчик с HTTP-клиентом, контекстом и конфигурацией. </summary>
    public PaymentCreationProcessor(
        HttpClient httpClient,
        OpenCRMContext context,
        ILogger logger,
        YooKassaConfig config,
        IPromotionPaymentCoordinator? promotionPaymentCoordinator = null)
    {
        this._httpClient = httpClient;
        this._context = context;
        this._logger = logger;
        this._promotionPaymentCoordinator = promotionPaymentCoordinator;
        var authBytes = Encoding.UTF8.GetBytes($"{config.ShopId}:{config.SecretKey}");
        this._httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
    }

    /// <summary> Создаёт платёж через провайдера с проверкой идемпотентности. </summary>
    public async Task<PaymentResponse> CreatePaymentAsync(
        string userId,
        int planId,
        decimal amount,
        string description,
        string operationType,
        PaymentBuyerDto buyer,
        string? returnUrl,
        PaymentPromotionDto? promotion,
        string? idempotenceKey,
        DateTime? requestTimestamp)
    {
        amount = PaymentCreationProcessor.NormalizeMoney(amount);
        var key = idempotenceKey ?? Guid.NewGuid().ToString("N");
        if (string.IsNullOrWhiteSpace(key) || key.Length > 64)
        {
            return PaymentResponse.CreateError("Ключ идемпотентности платежа должен содержать от 1 до 64 символов.");
        }

        var timestamp = PaymentCreationProcessor.NormalizeTimestamp(requestTimestamp ?? DateTime.UtcNow);
        var requestBody = this.BuildRequestBody(userId, planId, amount, description, returnUrl, timestamp);

        var operation = new PaymentCreationOperation
        {
            IdempotenceKey = key,
            UserId = userId,
            SubscriptionPlanId = planId,
            OperationType = operationType,
            Amount = amount,
            OriginalAmount = promotion == null ? null : PaymentCreationProcessor.NormalizeMoney(promotion.OriginalAmount),
            PromoDiscountAmount = promotion == null ? null : PaymentCreationProcessor.NormalizeMoney(promotion.DiscountAmount),
            PromoCodeId = promotion?.PromoCodeId,
            PromoCodeSnapshot = promotion?.PromoCode,
            PromoCodeApplicationId = promotion?.PromoCodeApplicationId,
            OrganizationId = promotion?.OrganizationId,
            Description = description,
            ReturnUrl = returnUrl,
            BuyerTypeCode = buyer.TypeCode,
            BuyerName = buyer.Name,
            BuyerInn = buyer.Inn,
            RequestTimestamp = timestamp,
            RequestBody = requestBody,
            CreatedAt = DateTime.UtcNow
        };

        PaymentCreationOperation prepared;
        var store = new PaymentCreationOperationStore(this._context, this._promotionPaymentCoordinator);
        try
        {
            prepared = await store.PrepareAsync(operation, requestTimestamp.HasValue);
        }
        catch (PaymentCreationConflictException)
        {
            return PaymentResponse.CreateError("Ключ идемпотентности уже использован с другими параметрами.");
        }
        catch (InvalidOperationException exception) when (operation.PromoCodeApplicationId.HasValue)
        {
            // Захват заявки промокода отменён транзакцией: очищаем отслеживаемое состояние и возвращаем причину.
            store.DiscardTrackedState(operation.Id, operation.ExternalPaymentId ?? string.Empty);
            return PaymentResponse.CreateError(exception.Message);
        }

        return await this.ProcessOperationAsync(prepared.Id);
    }

    /// <summary> Повторяет обработку отложенных операций создания платежей. </summary>
    public async Task RetryPendingOperationsAsync()
    {
        var operationIds = await new PaymentCreationOperationStore(this._context, this._promotionPaymentCoordinator).GetRetryableOperationIdsAsync(100);
        foreach (var operationId in operationIds)
        {
            try
            {
                await this.ProcessOperationAsync(operationId);
            }
            catch (Exception exception)
            {
                this._logger.LogError(exception, "Не удалось восстановить создание платежа {OperationId}.", operationId);
            }
        }
    }

    /// <summary> Возобновляет обработку операции по идентификатору. </summary>
    public Task<PaymentResponse> ResumeOperationAsync(Guid operationId) => this.ProcessOperationAsync(operationId);

    /// <summary> Обрабатывает одну операцию создания платежа. </summary>
    private async Task<PaymentResponse> ProcessOperationAsync(Guid operationId)
    {
        var store = new PaymentCreationOperationStore(this._context, this._promotionPaymentCoordinator);
        var lease = await store.AcquireLeaseAsync(operationId);
        if (lease == null)
        {
            var operation = await this._context.PaymentCreationOperations.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == operationId);

            if (operation?.Status == PaymentCreationOperationStatuses.Created)
            {
                return PaymentResponse.CreateSuccess(operation.PaymentLink, operation.ExternalPaymentId);
            }

            if (operation?.Status == PaymentCreationOperationStatuses.Rejected)
            {
                return PaymentResponse.CreateError("Платеж отклонен платежным сервисом.");
            }

            if (operation?.Status == ManualReviewStatus)
            {
                return PaymentResponse.CreateError("Платеж требует ручной проверки.");
            }

            return PaymentResponse.CreateError("Создание платежа уже выполняется. Повторите попытку позже.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "payments")
        {
            Content = new StringContent(lease.RequestBody, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Idempotence-Key", lease.IdempotenceKey);

        HttpResponseMessage? response = null;
        string content;
        try
        {
            response = await this._httpClient.SendAsync(request);
            content = await response.Content.ReadAsStringAsync();
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException)
        {
            response?.Dispose();
            this._logger.LogWarning(exception, "Неизвестный результат создания платежа по ключу {IdempotenceKey}.", lease.IdempotenceKey);
            await store.RecordFailureAsync(lease, PreparedStatus, exception.GetType().Name);

            return PaymentResponse.CreateError("Не удалось подтвердить результат создания платежа. Повторите попытку позже.");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                YooKassaPaymentResponse? payment;
                try
                {
                    payment = JsonSerializer.Deserialize<YooKassaPaymentResponse>(content);
                }
                catch (JsonException)
                {
                    payment = null;
                }

                if (payment == null || string.IsNullOrWhiteSpace(payment.Id))
                {
                    await store.RecordFailureAsync(lease, PreparedStatus, "malformed_success_response");
                    return PaymentResponse.CreateError("Не удалось подтвердить результат создания платежа. Повторите попытку позже.");
                }

                var validation = this.ValidateResponse(lease, payment);
                if (validation == ProviderResponseValidation.Malformed)
                {
                    await store.RecordFailureAsync(lease, PreparedStatus, "malformed_success_response");
                    return PaymentResponse.CreateError("Не удалось подтвердить результат создания платежа. Повторите попытку позже.");
                }

                if (validation == ProviderResponseValidation.Mismatch)
                {
                    await store.RecordFailureAsync(lease, ManualReviewStatus, "provider_response_mismatch");
                    return PaymentResponse.CreateError("Ответ платежного сервиса требует ручной проверки.");
                }

                try
                {
                    return await store.FinalizeSuccessAsync(lease, payment)
                        ?? PaymentResponse.CreateError("Не удалось сохранить платеж.");
                }
                catch
                {
                    store.DiscardTrackedState(lease.OperationId, payment.Id);
                    throw;
                }
            }

            var errorCode = PaymentCreationProcessor.TryGetErrorCode(content);
            if (response.StatusCode == HttpStatusCode.BadRequest && errorCode == "invalid_request")
            {
                await store.RecordFailureAsync(lease, PaymentCreationOperationStatuses.Rejected, errorCode);
                return PaymentResponse.CreateError("Платеж отклонен платежным сервисом.");
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                await store.RecordFailureAsync(lease, ManualReviewStatus, errorCode ?? ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
                return PaymentResponse.CreateError("Настройки платежного сервиса требуют ручной проверки.");
            }

            await store.RecordFailureAsync(
                lease,
                PreparedStatus,
                errorCode ?? ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
            this._logger.LogWarning("YooKassa временно не создала платеж: HTTP {StatusCode}.", (int)response.StatusCode);

            return PaymentResponse.CreateError("Не удалось подтвердить результат создания платежа. Повторите попытку позже.");
        }
    }

    /// <summary> Проверяет ответ провайдера на корректность и соответствие запросу. </summary>
    private ProviderResponseValidation ValidateResponse(PaymentCreationLease lease, YooKassaPaymentResponse payment)
    {
        if (payment.CreatedAt == default || payment.Amount == null || payment.Metadata == null)
        {
            return ProviderResponseValidation.Malformed;
        }

        if (!decimal.TryParse(payment.Amount.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            return ProviderResponseValidation.Malformed;
        }

        if (amount != lease.Amount || payment.Amount.Currency != "RUB"
            || !payment.Metadata.TryGetValue("userId", out var userId)
            || userId != lease.UserId
            || !payment.Metadata.TryGetValue("planId", out var planId)
            || planId != lease.PlanId.ToString(CultureInfo.InvariantCulture)
            || !payment.Metadata.TryGetValue("timestamp", out var timestamp)
            || timestamp != lease.RequestTimestamp.ToString("O", CultureInfo.InvariantCulture))
        {
            return ProviderResponseValidation.Mismatch;
        }

        if (payment.Status == "pending" && !payment.Paid)
        {
            return PaymentCreationProcessor.HasValidConfirmation(payment.Confirmation)
                ? ProviderResponseValidation.Valid
                : ProviderResponseValidation.Malformed;
        }

        if (payment.Status is "waiting_for_capture" or "succeeded" && payment.Paid)
        {
            return payment.Confirmation == null || PaymentCreationProcessor.HasValidConfirmation(payment.Confirmation)
                ? ProviderResponseValidation.Valid
                : ProviderResponseValidation.Mismatch;
        }

        return ProviderResponseValidation.Malformed;
    }

    /// <summary> Проверяет наличие корректного подтверждения в ответе. </summary>
    private static bool HasValidConfirmation(YooKassaConfirmation? confirmation) =>
        confirmation?.Type == "redirect"
        && Uri.TryCreate(confirmation.ConfirmationUrl, UriKind.Absolute, out var confirmationUri)
        && confirmationUri.Scheme == Uri.UriSchemeHttps;

    /// <summary> Формирует тело запроса создания платежа. </summary>
    private string BuildRequestBody(
        string userId,
        int planId,
        decimal amount,
        string description,
        string? returnUrl,
        DateTime requestTimestamp)
    {
        var paymentData = new
        {
            amount = new
            {
                value = amount.ToString("F2", CultureInfo.InvariantCulture),
                currency = "RUB"
            },
            capture = true,
            confirmation = new
            {
                type = "redirect",
                return_url = returnUrl
            },
            description,
            metadata = new Dictionary<string, string>
            {
                { "userId", userId },
                { "planId", planId.ToString(CultureInfo.InvariantCulture) },
                { "timestamp", requestTimestamp.ToString("O", CultureInfo.InvariantCulture) }
            }
        };

        return JsonSerializer.Serialize(paymentData, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    /// <summary> Приводит метку времени к нормализованному виду. </summary>
    private static DateTime NormalizeTimestamp(DateTime value) =>
        DateTime.SpecifyKind(value.AddTicks(-(value.Ticks % 10)), value.Kind);

    /// <summary> Нормализует денежную сумму. </summary>
    private static decimal NormalizeMoney(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary> Извлекает код ошибки из ответа провайдера. </summary>
    private static string? TryGetErrorCode(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (document.RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String)
            {
                return code.GetString();
            }

            if (document.RootElement.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
            {
                return type.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    /// <summary> Результат проверки ответа провайдера. </summary>
    private enum ProviderResponseValidation
    {
        /// <summary> Ответ корректен. </summary>
        Valid,

        /// <summary> Ответ имеет некорректный формат. </summary>
        Malformed,

        /// <summary> Ответ не соответствует запросу. </summary>
        Mismatch
    }
}
