using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Payments.Service.Configuration;
using Payments.Service.Domain;
using Microsoft.Extensions.Options;

namespace Payments.Service.Providers;

/// <summary> Выполняет проверенные HTTP-запросы к ЮKassa от имени одного магазина. </summary>
public sealed class YooKassaPaymentProvider : IPaymentProvider
{
    private const int MaximumResponseBytes = 65_536;

    private readonly HttpClient _httpClient;
    private readonly PaymentOptions _options;

    public YooKassaPaymentProvider(HttpClient httpClient, IOptions<PaymentOptions> options)
    {
        this._httpClient = httpClient;
        this._options = options.Value;

        var isTestMode = this._options.Mode == "YooKassaTest";
        if (this._options.Mode is not ("YooKassaTest" or "YooKassaLive")
            || this._options.IsTest != isTestMode
            || string.IsNullOrWhiteSpace(this._options.MerchantAccountId)
            || !this._options.MerchantAccountId.All(char.IsAsciiDigit)
            || !this._options.SecretKey.StartsWith(isTestMode ? "test_" : "live_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Режим, магазин и ключ ЮKassa должны соответствовать друг другу.");
        }

        this._httpClient.BaseAddress ??= new Uri("https://api.yookassa.ru/v3/");
    }

    public string BuildPaymentRequest(PaymentOrder order)
    {
        this.EnsureTestMerchant(order);

        return BuildPaymentRequestBody(order);
    }

    internal static string BuildPaymentRequestBody(PaymentOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (order.AmountMinor <= 0 || order.Currency != "RUB")
        {
            throw new PaymentProviderException("invalid_payment_order", isDefinitive: true);
        }

        return JsonSerializer.Serialize(new
        {
            amount = new
            {
                value = (order.AmountMinor / 100m).ToString("F2", CultureInfo.InvariantCulture),
                currency = order.Currency
            },
            capture = true,
            confirmation = new
            {
                type = "redirect",
                return_url = order.ReturnUrl
            },
            description = order.Description,
            metadata = new Dictionary<string, string>
            {
                ["paymentOrderId"] = order.Id.ToString(),
                ["purchaseId"] = order.PurchaseId.ToString(),
                ["applicationId"] = order.ApplicationId,
                ["merchantAccountId"] = order.MerchantAccountId
            }
        });
    }

    public string BuildRefundRequest(PaymentOrder order, long amountMinor, string reason)
    {
        this.EnsureTestMerchant(order);

        return BuildRefundRequestBody(order, amountMinor, reason);
    }

    internal static string BuildRefundRequestBody(PaymentOrder order, long amountMinor, string reason)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountMinor);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (order.ProviderPaymentId is null || amountMinor > order.AmountMinor - order.RefundedAmountMinor)
        {
            throw new PaymentProviderException("invalid_refund_order", isDefinitive: true);
        }

        return JsonSerializer.Serialize(new
        {
            payment_id = order.ProviderPaymentId,
            amount = new
            {
                value = (amountMinor / 100m).ToString("F2", CultureInfo.InvariantCulture),
                currency = order.Currency
            },
            description = reason
        });
    }

    public async Task<ProviderPayment> CreatePaymentAsync(
        string requestBody,
        string idempotenceKey,
        CancellationToken cancellationToken)
    {
        ValidateIdempotenceKey(idempotenceKey);

        return await this.PostAsync<ProviderPayment>("payments", requestBody, idempotenceKey, ParsePayment, cancellationToken);
    }

    public Task<ProviderPayment> GetPaymentAsync(string paymentId, CancellationToken cancellationToken)
    {
        ValidateObjectId(paymentId);

        return this.GetAsync($"payments/{Uri.EscapeDataString(paymentId)}", ParsePayment, cancellationToken);
    }

    public async Task<ProviderRefund> CreateRefundAsync(
        string requestBody,
        string idempotenceKey,
        CancellationToken cancellationToken)
    {
        ValidateIdempotenceKey(idempotenceKey);

        return await this.PostAsync<ProviderRefund>("refunds", requestBody, idempotenceKey, ParseRefund, cancellationToken);
    }

    public Task<ProviderRefund> GetRefundAsync(string refundId, CancellationToken cancellationToken)
    {
        ValidateObjectId(refundId);

        return this.GetAsync($"refunds/{Uri.EscapeDataString(refundId)}", ParseRefund, cancellationToken);
    }

    private static ProviderPayment ParsePayment(JsonElement root)
    {
        var id = ReadRequiredString(root, "id");
        var status = ReadRequiredString(root, "status");
        if (status is not ("pending" or "waiting_for_capture" or "succeeded" or "canceled"))
        {
            throw InvalidResponse("payment_status_unknown");
        }

        var amount = ReadAmount(root.GetProperty("amount"));
        var metadata = root.GetProperty("metadata");
        var recipient = root.GetProperty("recipient");
        var refundedAmountMinor = 0L;
        if (root.TryGetProperty("refunded_amount", out var refundedAmount) && refundedAmount.ValueKind != JsonValueKind.Null)
        {
            var refundTotal = ReadAmount(refundedAmount);
            if (refundTotal.Currency != "RUB")
            {
                throw InvalidResponse("provider_currency_unsupported");
            }

            refundedAmountMinor = refundTotal.Minor;
        }

        string? confirmationUrl = null;
        if (root.TryGetProperty("confirmation", out var confirmation) && confirmation.ValueKind != JsonValueKind.Null)
        {
            if (ReadRequiredString(confirmation, "type") != "redirect")
            {
                throw InvalidResponse("confirmation_type_invalid");
            }

            confirmationUrl = ReadRequiredString(confirmation, "confirmation_url");
            if (!Uri.TryCreate(confirmationUrl, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || uri.UserInfo.Length != 0
                || uri.Host is not ("yookassa.ru" or "yoomoney.ru")
                    && !uri.Host.EndsWith(".yookassa.ru", StringComparison.OrdinalIgnoreCase)
                    && !uri.Host.EndsWith(".yoomoney.ru", StringComparison.OrdinalIgnoreCase))
            {
                throw InvalidResponse("confirmation_url_untrusted");
            }
        }

        if (amount.Currency != "RUB")
        {
            throw InvalidResponse("provider_currency_unsupported");
        }

        if (status == "pending" && confirmationUrl == null)
        {
            throw InvalidResponse("confirmation_url_missing");
        }

        return new ProviderPayment(
            id,
            status,
            amount.Minor,
            amount.Currency,
            ReadRequiredBoolean(root, "paid"),
            ReadRequiredBoolean(root, "test"),
            refundedAmountMinor,
            confirmationUrl,
            ReadRequiredString(metadata, "paymentOrderId"),
            ReadRequiredString(recipient, "account_id"));
    }

    private static ProviderRefund ParseRefund(JsonElement root)
    {
        var amount = ReadAmount(root.GetProperty("amount"));
        if (amount.Currency != "RUB")
        {
            throw InvalidResponse("provider_currency_unsupported");
        }

        return new ProviderRefund(
            ReadRequiredString(root, "id"),
            ReadRequiredString(root, "payment_id"),
            ReadRequiredString(root, "status") switch
            {
                "pending" => "pending",
                "succeeded" => "succeeded",
                "canceled" => "canceled",
                _ => throw InvalidResponse("refund_status_unknown")
            },
            amount.Minor,
            amount.Currency);
    }

    private async Task<T> PostAsync<T>(
        string path,
        string requestBody,
        string idempotenceKey,
        Func<JsonElement, T> parser,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestBody);
        using var request = this.CreateRequest(HttpMethod.Post, path);
        request.Headers.TryAddWithoutValidation("Idempotence-Key", idempotenceKey);
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        var (statusCode, responseBody) = await this.SendAndReadAsync(request, cancellationToken);
        if (!IsSuccessStatusCode(statusCode))
        {
            throw CreateProviderException(statusCode, responseBody);
        }

        return ParseJson(responseBody, parser);
    }

    private async Task<T> GetAsync<T>(string path, Func<JsonElement, T> parser, CancellationToken cancellationToken)
    {
        using var request = this.CreateRequest(HttpMethod.Get, path);
        var (statusCode, responseBody) = await this.SendAndReadAsync(request, cancellationToken);
        if (!IsSuccessStatusCode(statusCode))
        {
            throw CreateProviderException(statusCode, responseBody);
        }

        return ParseJson(responseBody, parser);
    }

    private async Task<(System.Net.HttpStatusCode StatusCode, string Body)> SendAndReadAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (this._httpClient.Timeout != Timeout.InfiniteTimeSpan)
        {
            timeoutSource.CancelAfter(this._httpClient.Timeout);
        }

        try
        {
            using var response = await this._httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token);
            var body = await ReadResponseAsync(response, timeoutSource.Token);

            return (response.StatusCode, body);
        }
        catch (HttpRequestException)
        {
            throw new PaymentProviderException("provider_connection_unknown");
        }
        catch (IOException)
        {
            throw new PaymentProviderException("provider_connection_unknown");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new PaymentProviderException("provider_timeout_unknown");
        }
    }

    private static bool IsSuccessStatusCode(System.Net.HttpStatusCode statusCode) =>
        (int)statusCode is >= 200 and <= 299;

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        if (this._httpClient.BaseAddress?.Scheme != Uri.UriSchemeHttps
            || this._httpClient.BaseAddress.Host != "api.yookassa.ru")
        {
            throw new InvalidOperationException("Разрешены только запросы к официальному HTTPS API ЮKassa.");
        }

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{this._options.MerchantAccountId}:{this._options.SecretKey}"));
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return request;
    }

    private void EnsureTestMerchant(PaymentOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (order.IsTest != this._options.IsTest
            || !string.Equals(order.MerchantAccountId, this._options.MerchantAccountId, StringComparison.Ordinal))
        {
            throw new PaymentProviderException("payment_merchant_configuration_mismatch", requiresManualReview: true);
        }
    }

    private static async Task<string> ReadResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
        {
            throw InvalidResponse("provider_response_too_large");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        while (true)
        {
            var bytesRead = await stream.ReadAsync(chunk, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            if (buffer.Length + bytesRead > MaximumResponseBytes)
            {
                throw InvalidResponse("provider_response_too_large");
            }

            buffer.Write(chunk, 0, bytesRead);
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }
        catch (DecoderFallbackException)
        {
            throw InvalidResponse("provider_response_invalid");
        }
    }

    private static T ParseJson<T>(string content, Func<JsonElement, T> parser)
    {
        try
        {
            using var document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 32 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw InvalidResponse("provider_response_invalid");
            }

            return parser(document.RootElement);
        }
        catch (JsonException)
        {
            throw InvalidResponse("provider_response_invalid");
        }
        catch (KeyNotFoundException)
        {
            throw InvalidResponse("provider_response_incomplete");
        }
        catch (InvalidOperationException)
        {
            throw InvalidResponse("provider_response_incomplete");
        }
        catch (FormatException)
        {
            throw InvalidResponse("provider_response_invalid");
        }
        catch (OverflowException)
        {
            throw InvalidResponse("provider_amount_invalid");
        }
    }

    private static PaymentProviderException CreateProviderException(System.Net.HttpStatusCode statusCode, string body)
    {
        if (statusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
            or System.Net.HttpStatusCode.Conflict)
        {
            return new PaymentProviderException("provider_configuration_or_key_conflict", requiresManualReview: true);
        }

        if (statusCode == System.Net.HttpStatusCode.BadRequest && TryReadDefinitiveError(body, out var errorCode))
        {
            return new PaymentProviderException($"provider_rejected:{errorCode}", isDefinitive: true);
        }

        return new PaymentProviderException($"provider_http_{(int)statusCode}_unknown");
    }

    private static bool TryReadDefinitiveError(string body, out string errorCode)
    {
        errorCode = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("code", out var code)
                || code.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var typeValue = type.GetString();
            var codeValue = code.GetString();
            if (typeValue != "error" || string.IsNullOrWhiteSpace(codeValue)
                || !IsSafeErrorCode(codeValue))
            {
                return false;
            }

            errorCode = codeValue;

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsSafeErrorCode(string value) =>
        value.Length <= 64 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private static (long Minor, string Currency) ReadAmount(JsonElement amount)
    {
        if (amount.ValueKind != JsonValueKind.Object)
        {
            throw InvalidResponse("provider_amount_invalid");
        }

        var value = ReadRequiredString(amount, "value");
        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var major)
            || major < 0
            || major != decimal.Truncate(major * 100m) / 100m)
        {
            throw InvalidResponse("provider_amount_invalid");
        }

        return (checked((long)(major * 100m)), ReadRequiredString(amount, "currency"));
    }

    private static string ReadRequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw InvalidResponse("provider_response_incomplete");
        }

        return property.GetString()!;
    }

    private static bool ReadRequiredBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw InvalidResponse("provider_response_incomplete");
        }

        return property.GetBoolean();
    }

    private static void ValidateIdempotenceKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 64)
        {
            throw new PaymentProviderException("idempotence_key_invalid", isDefinitive: true);
        }
    }

    private static void ValidateObjectId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
        {
            throw new PaymentProviderException("provider_object_id_invalid", requiresManualReview: true);
        }
    }

    private static PaymentProviderException InvalidResponse(string code) => new(code);
}
