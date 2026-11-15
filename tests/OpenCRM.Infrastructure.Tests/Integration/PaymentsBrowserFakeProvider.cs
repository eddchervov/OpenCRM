using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Payments.Service.Domain;
using Payments.Service.Providers;

namespace OpenCRM.Infrastructure.Tests.Integration;

internal sealed class PaymentsBrowserFakeProvider(string merchantAccountId, string baseUrl) : IPaymentProvider
{
    private readonly ConcurrentDictionary<string, FakePayment> _paymentsByKey = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _paymentKeysById = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, FakeRefund> _refundsByKey = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _refundKeysById = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _lostCreateResponses = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private string _mode = "normal";
    private int _acceptedPayments;
    private int _createPaymentPostCalls;
    private int _createRefundPostCalls;

    public string BuildPaymentRequest(PaymentOrder order)
    {
        if (!order.IsTest || order.MerchantAccountId != merchantAccountId)
        {
            throw new PaymentProviderException("qa_merchant_mismatch", requiresManualReview: true);
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
                ["paymentOrderId"] = order.Id.ToString("D"),
                ["purchaseId"] = order.PurchaseId.ToString("D"),
                ["applicationId"] = order.ApplicationId,
                ["merchantAccountId"] = order.MerchantAccountId
            }
        });
    }

    public string BuildRefundRequest(PaymentOrder order, long amountMinor, string reason) => JsonSerializer.Serialize(new
    {
        payment_id = order.ProviderPaymentId,
        amount = new
        {
            value = (amountMinor / 100m).ToString("F2", CultureInfo.InvariantCulture),
            currency = order.Currency
        },
        description = reason
    });

    public Task<ProviderPayment> CreatePaymentAsync(string requestBody, string idempotenceKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref this._createPaymentPostCalls);
        if (Volatile.Read(ref this._mode) == "unavailable")
        {
            throw new PaymentProviderException("qa_provider_unavailable");
        }

        using var document = JsonDocument.Parse(requestBody);
        var root = document.RootElement;
        var amount = root.GetProperty("amount");
        var amountMinor = decimal.ToInt64(decimal.Parse(
            amount.GetProperty("value").GetString()!,
            CultureInfo.InvariantCulture) * 100m);
        var currency = amount.GetProperty("currency").GetString()!;
        var metadata = root.GetProperty("metadata");
        var paymentOrderId = metadata.GetProperty("paymentOrderId").GetString()!;
        var purchaseId = metadata.GetProperty("purchaseId").GetString()!;
        var applicationId = metadata.GetProperty("applicationId").GetString()!;
        var requestMerchantId = metadata.GetProperty("merchantAccountId").GetString()!;
        var returnUrl = root.GetProperty("confirmation").GetProperty("return_url").GetString()!;
        if (requestMerchantId != merchantAccountId)
        {
            throw new PaymentProviderException("qa_merchant_mismatch", requiresManualReview: true);
        }

        this.ValidateLocalReturnUrl(returnUrl, purchaseId);

        FakePayment payment;
        lock (this._sync)
        {
            if (!this._paymentsByKey.TryGetValue(idempotenceKey, out payment!))
            {
                var paymentId = "qa-pay-" + HashId(idempotenceKey);
                payment = new FakePayment(
                    paymentId,
                    idempotenceKey,
                    requestBody,
                    amountMinor,
                    currency,
                    paymentOrderId,
                    purchaseId,
                    applicationId,
                    returnUrl,
                    "pending",
                    false,
                    0);
                this._paymentsByKey[idempotenceKey] = payment;
                this._paymentKeysById[paymentId] = idempotenceKey;
                Interlocked.Increment(ref this._acceptedPayments);
            }
            else if (payment.RequestBody != requestBody)
            {
                throw new PaymentProviderException("qa_idempotence_conflict", requiresManualReview: true);
            }
        }

        if (Volatile.Read(ref this._mode) == "timeout_after_accept"
            && this._lostCreateResponses.TryAdd(idempotenceKey, 0))
        {
            throw new PaymentProviderException("provider_timeout_unknown");
        }

        return Task.FromResult(this.ToProviderPayment(payment));
    }

    public Task<ProviderPayment> GetPaymentAsync(string paymentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!this._paymentKeysById.TryGetValue(paymentId, out var key)
            || !this._paymentsByKey.TryGetValue(key, out var payment))
        {
            throw new PaymentProviderException("qa_payment_not_found", requiresManualReview: true);
        }

        return Task.FromResult(this.ToProviderPayment(payment));
    }

    public Task<ProviderRefund> CreateRefundAsync(string requestBody, string idempotenceKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref this._createRefundPostCalls);
        if (Volatile.Read(ref this._mode) == "unavailable")
        {
            throw new PaymentProviderException("qa_provider_unavailable");
        }

        using var document = JsonDocument.Parse(requestBody);
        var root = document.RootElement;
        var paymentId = root.GetProperty("payment_id").GetString()!;
        var amount = root.GetProperty("amount");
        var amountMinor = decimal.ToInt64(decimal.Parse(
            amount.GetProperty("value").GetString()!,
            CultureInfo.InvariantCulture) * 100m);
        var currency = amount.GetProperty("currency").GetString()!;
        FakePayment payment;
        string paymentKey;
        FakeRefund refund;
        lock (this._sync)
        {
            if (!this._paymentKeysById.TryGetValue(paymentId, out paymentKey!)
                || !this._paymentsByKey.TryGetValue(paymentKey, out payment!)
                || payment.Status != "succeeded"
                || payment.Currency != currency
                || amountMinor <= 0
                || amountMinor > payment.AmountMinor - payment.RefundedAmountMinor)
            {
                throw new PaymentProviderException("qa_refund_invalid", isDefinitive: true);
            }

            if (!this._refundsByKey.TryGetValue(idempotenceKey, out refund!))
            {
                refund = new FakeRefund(
                    "qa-refund-" + HashId(idempotenceKey),
                    idempotenceKey,
                    requestBody,
                    paymentId,
                    amountMinor,
                    currency,
                    "succeeded");
                this._refundKeysById[refund.Id] = idempotenceKey;
                this._refundsByKey[idempotenceKey] = refund;
                this._paymentsByKey[paymentKey] = payment with { RefundedAmountMinor = payment.RefundedAmountMinor + amountMinor };
            }
            else if (refund.RequestBody != requestBody)
            {
                throw new PaymentProviderException("qa_idempotence_conflict", requiresManualReview: true);
            }
        }

        return Task.FromResult(ToProviderRefund(refund));
    }

    public Task<ProviderRefund> GetRefundAsync(string refundId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!this._refundKeysById.TryGetValue(refundId, out var key)
            || !this._refundsByKey.TryGetValue(key, out var refund))
        {
            throw new PaymentProviderException("qa_refund_not_found", requiresManualReview: true);
        }

        return Task.FromResult(ToProviderRefund(refund));
    }

    public bool TrySetMode(string? mode)
    {
        if (mode is not ("normal" or "unavailable" or "timeout_after_accept" or "recovery"))
        {
            return false;
        }

        Volatile.Write(ref this._mode, mode == "recovery" ? "normal" : mode);
        return true;
    }

    public FakePayment? FindPayment(string paymentId)
    {
        if (!this._paymentKeysById.TryGetValue(paymentId, out var key))
        {
            return null;
        }

        return this._paymentsByKey.TryGetValue(key, out var payment) ? payment : null;
    }

    public string? MarkPaid(string paymentId) => this.UpdatePayment(paymentId, "succeeded", true)?.ReturnUrl;

    public string? CancelPayment(string paymentId) => this.UpdatePayment(paymentId, "canceled", false)?.ReturnUrl;

    public void MarkAllPaymentsPaid()
    {
        foreach (var payment in this._paymentsByKey.Values)
        {
            this.UpdatePayment(payment.Id, "succeeded", true);
        }
    }

    public object GetSnapshot() => new
    {
        mode = Volatile.Read(ref this._mode),
        acceptedPayments = Volatile.Read(ref this._acceptedPayments),
        createPaymentPostCalls = Volatile.Read(ref this._createPaymentPostCalls),
        createRefundPostCalls = Volatile.Read(ref this._createRefundPostCalls),
        paymentAmountMinor = this._paymentsByKey.Values.Sum(payment => payment.AmountMinor),
        refundedAmountMinor = this._paymentsByKey.Values.Sum(payment => payment.RefundedAmountMinor),
        payments = this._paymentsByKey.Values.Select(payment => new
        {
            payment.Id,
            payment.PurchaseId,
            payment.AmountMinor,
            payment.Currency,
            payment.Status,
            payment.RefundedAmountMinor
        })
    };

    private FakePayment? UpdatePayment(string paymentId, string status, bool paid)
    {
        lock (this._sync)
        {
            if (!this._paymentKeysById.TryGetValue(paymentId, out var key)
                || !this._paymentsByKey.TryGetValue(key, out var payment))
            {
                return null;
            }

            var updated = payment with { Status = status, Paid = paid };
            this._paymentsByKey[key] = updated;

            return updated;
        }
    }

    private ProviderPayment ToProviderPayment(FakePayment payment) => new(
        payment.Id,
        payment.Status,
        payment.AmountMinor,
        payment.Currency,
        payment.Paid,
        true,
        payment.RefundedAmountMinor,
        payment.Status == "pending" ? $"{baseUrl}/__qa/checkout/{Uri.EscapeDataString(payment.Id)}" : null,
        payment.OrderId,
        merchantAccountId);

    private static ProviderRefund ToProviderRefund(FakeRefund refund) => new(
        refund.Id,
        refund.PaymentId,
        refund.Status,
        refund.AmountMinor,
        refund.Currency);

    private static string HashId(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..20].ToLowerInvariant();

    private void ValidateLocalReturnUrl(string returnUrl, string purchaseId)
    {
        var expected = $"{PaymentsBrowserHarness.MainApplicationReturnUrl}?purchaseId={purchaseId}";
        if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.Host != "localhost"
            || uri.Port != 7244
            || uri.AbsolutePath != "/payment/complete"
            || returnUrl != expected)
        {
            throw new PaymentProviderException("qa_return_url_not_localhost", isDefinitive: true);
        }
    }

    internal sealed record FakePayment(
        string Id,
        string IdempotenceKey,
        string RequestBody,
        long AmountMinor,
        string Currency,
        string OrderId,
        string PurchaseId,
        string ApplicationId,
        string ReturnUrl,
        string Status,
        bool Paid,
        long RefundedAmountMinor);

    private sealed record FakeRefund(
        string Id,
        string IdempotenceKey,
        string RequestBody,
        string PaymentId,
        long AmountMinor,
        string Currency,
        string Status);
}
