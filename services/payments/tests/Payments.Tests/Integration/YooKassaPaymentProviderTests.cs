using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Payments.Service.Configuration;
using Payments.Service.Domain;
using Payments.Service.Providers;

namespace Payments.Tests.Integration;

public sealed class YooKassaPaymentProviderTests
{
    private static readonly string s_paymentResponse = """
        {
          "id": "provider-payment-1",
          "status": "pending",
          "paid": false,
          "amount": { "value": "1250.50", "currency": "RUB" },
          "refunded_amount": { "value": "0.00", "currency": "RUB" },
          "confirmation": {
            "type": "redirect",
            "confirmation_url": "https://yookassa.ru/checkout/payment/1"
          },
          "recipient": { "account_id": "123456" },
          "metadata": { "paymentOrderId": "00000000-0000-0000-0000-000000000011" },
          "test": true
        }
        """;

    [Fact]
    public async Task CreatePaymentAsync_UsesPersistedBodyAndIdempotenceKey()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(s_paymentResponse, Encoding.UTF8, "application/json")
        });
        using var client = CreateClient(handler);
        var provider = CreateProvider(client);
        var requestBody = provider.BuildPaymentRequest(CreateOrder());

        var payment = await provider.CreatePaymentAsync(requestBody, "persistent-operation-key", CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("https://api.yookassa.ru/v3/payments", handler.RequestUri?.ToString());
        Assert.Equal("persistent-operation-key", handler.IdempotenceKey);
        Assert.Equal(requestBody, handler.RequestBody);
        Assert.StartsWith("Basic ", handler.Authorization);
        Assert.Equal("provider-payment-1", payment.Id);
        Assert.Equal(125_050, payment.AmountMinor);
        Assert.Equal("123456", payment.MerchantAccountId);
        Assert.True(payment.IsTest);
    }

    [Fact]
    public void BuildPaymentRequest_UsesExactRubleAmountAndOrderMetadata()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = CreateClient(handler);
        var provider = CreateProvider(client);

        using var document = JsonDocument.Parse(provider.BuildPaymentRequest(CreateOrder()));
        var root = document.RootElement;

        Assert.Equal("1250.50", root.GetProperty("amount").GetProperty("value").GetString());
        Assert.Equal("RUB", root.GetProperty("amount").GetProperty("currency").GetString());
        Assert.True(root.GetProperty("capture").GetBoolean());
        Assert.Equal("redirect", root.GetProperty("confirmation").GetProperty("type").GetString());
        Assert.Equal("00000000-0000-0000-0000-000000000011",
            root.GetProperty("metadata").GetProperty("paymentOrderId").GetString());
        Assert.Equal("123456", root.GetProperty("metadata").GetProperty("merchantAccountId").GetString());
    }

    [Fact]
    public async Task GetPaymentAsync_RejectsUntrustedObjectIdBeforeSending()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = CreateClient(handler);
        var provider = CreateProvider(client);

        var exception = await Assert.ThrowsAsync<PaymentProviderException>(
            () => provider.GetPaymentAsync("../secret", CancellationToken.None));

        Assert.Equal("provider_object_id_invalid", exception.Code);
        Assert.Null(handler.Method);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "{\"type\":\"error\",\"code\":\"invalid_request\"}", false, false)]
    [InlineData(HttpStatusCode.BadRequest, "{\"type\":\"error\"}", false, false)]
    [InlineData(HttpStatusCode.BadRequest, "{\"type\":\"error\",\"code\":\"invalid_request\"}", true, false)]
    [InlineData(HttpStatusCode.Unauthorized, "{}", false, true)]
    [InlineData(HttpStatusCode.Conflict, "{}", false, true)]
    public async Task CreatePaymentAsync_ClassifiesUncertainAndManualResponses(
        HttpStatusCode statusCode,
        string responseBody,
        bool isDefinitive,
        bool requiresManualReview)
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        });
        using var client = CreateClient(handler);
        var provider = CreateProvider(client);

        var exception = await Assert.ThrowsAsync<PaymentProviderException>(
            () => provider.CreatePaymentAsync("{}", Guid.NewGuid().ToString(), CancellationToken.None));

        Assert.Equal(isDefinitive, exception.IsDefinitive);
        Assert.Equal(requiresManualReview, exception.RequiresManualReview);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not-json")]
    [InlineData("{\"id\":\"p1\",\"status\":\"future-status\"}")]
    [InlineData("{\"id\":\"p1\",\"status\":\"succeeded\",\"paid\":true,\"amount\":{\"value\":\"1250.5\",\"currency\":\"USD\"},\"metadata\":{\"paymentOrderId\":\"x\"},\"recipient\":{\"account_id\":\"shop-1\"},\"test\":true}")]
    public async Task CreatePaymentAsync_TreatsIncompleteOrInvalidResponseAsUnknown(string responseBody)
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        });
        using var client = CreateClient(handler);
        var provider = CreateProvider(client);

        var exception = await Assert.ThrowsAsync<PaymentProviderException>(
            () => provider.CreatePaymentAsync("{}", Guid.NewGuid().ToString(), CancellationToken.None));

        Assert.False(exception.IsDefinitive);
        Assert.False(exception.RequiresManualReview);
    }

    [Fact]
    public async Task GetPaymentAsync_RejectsExternalRedirectUrl()
    {
        var responseBody = s_paymentResponse.Replace("https://yookassa.ru/checkout", "https://attacker.example/checkout", StringComparison.Ordinal);
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        });
        using var client = CreateClient(handler);
        var provider = CreateProvider(client);

        var exception = await Assert.ThrowsAsync<PaymentProviderException>(
            () => provider.GetPaymentAsync("payment-1", CancellationToken.None));

        Assert.Equal("confirmation_url_untrusted", exception.Code);
        Assert.False(exception.IsDefinitive);
    }

    [Fact]
    public async Task CreateRefundAsync_UsesStableKeyAndRubleAmount()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                { "id": "refund-1", "payment_id": "provider-payment-1", "status": "pending", "amount": { "value": "250.00", "currency": "RUB" } }
                """, Encoding.UTF8, "application/json")
        });
        using var client = CreateClient(handler);
        var provider = CreateProvider(client);
        var order = CreateOrder();
        order.ProviderPaymentId = "provider-payment-1";
        var body = provider.BuildRefundRequest(order, 25_000, "Исправление оплаты");

        var refund = await provider.CreateRefundAsync(body, "persistent-refund-key", CancellationToken.None);

        using var request = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("RUB", request.RootElement.GetProperty("amount").GetProperty("currency").GetString());
        Assert.Equal("250.00", request.RootElement.GetProperty("amount").GetProperty("value").GetString());
        Assert.Equal("persistent-refund-key", handler.IdempotenceKey);
        Assert.Equal("refund-1", refund.Id);
    }

    [Fact]
    public async Task CreatePaymentAsync_TimesOutWhenResponseBodyNeverCompletes_AndKeepsIdempotenceKey()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new NeverCompletingStream())
        });
        using var client = CreateClient(handler);
        client.Timeout = TimeSpan.FromMilliseconds(100);
        var provider = CreateProvider(client);

        var exception = await Assert.ThrowsAsync<PaymentProviderException>(() =>
            provider.CreatePaymentAsync("persisted-body", "persistent-key", CancellationToken.None));

        Assert.Equal("provider_timeout_unknown", exception.Code);
        Assert.False(exception.IsDefinitive);
        Assert.Equal("persistent-key", handler.IdempotenceKey);
        Assert.Equal("persisted-body", handler.RequestBody);
    }

    [Fact]
    public async Task GetPaymentAsync_ClassifiesPartialBodyIoFailureAsUnknownTransportFailure()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new PartialThenFailingStream())
        });
        using var client = CreateClient(handler);
        var provider = CreateProvider(client);

        var exception = await Assert.ThrowsAsync<PaymentProviderException>(
            () => provider.GetPaymentAsync("payment-1", CancellationToken.None));

        Assert.Equal("provider_connection_unknown", exception.Code);
        Assert.False(exception.IsDefinitive);
    }

    [Fact]
    public async Task CreatePaymentAsync_PreservesCallerCancellationWhileReadingBody()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new NeverCompletingStream())
        });
        using var client = CreateClient(handler);
        client.Timeout = Timeout.InfiniteTimeSpan;
        var provider = CreateProvider(client);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.CreatePaymentAsync("persisted-body", "persistent-key", cancellation.Token));
    }

    private static HttpClient CreateClient(RecordingHandler handler) => new(handler)
    {
        BaseAddress = new Uri("https://api.yookassa.ru/v3/")
    };

    [Fact]
    public async Task LiveModeSendsOnlyLivePaymentAndAcceptsLiveProviderResponse()
    {
        var liveResponse = s_paymentResponse.Replace("\"test\": true", "\"test\": false", StringComparison.Ordinal);
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(liveResponse, Encoding.UTF8, "application/json")
        });
        using var client = CreateClient(handler);
        var provider = CreateProvider(client, "YooKassaLive", isTest: false, secretKey: "live_unit-test-secret");
        var order = CreateOrder();
        order.IsTest = false;

        var requestBody = provider.BuildPaymentRequest(order);
        var payment = await provider.CreatePaymentAsync(requestBody, "live-payment-key", CancellationToken.None);

        Assert.False(payment.IsTest);
        Assert.Equal("123456", payment.MerchantAccountId);
        Assert.Equal("live-payment-key", handler.IdempotenceKey);
    }

    [Fact]
    public void LiveModeRejectsTestOrderBeforeBuildingProviderRequest()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = CreateClient(handler);
        var provider = CreateProvider(client, "YooKassaLive", isTest: false, secretKey: "live_unit-test-secret");

        var exception = Assert.Throws<PaymentProviderException>(() => provider.BuildPaymentRequest(CreateOrder()));

        Assert.Equal("payment_merchant_configuration_mismatch", exception.Code);
        Assert.Null(handler.Method);
    }

    private static YooKassaPaymentProvider CreateProvider(
        HttpClient client,
        string mode = "YooKassaTest",
        bool isTest = true,
        string secretKey = "test_unit-test-secret") => new(
        client,
        Options.Create(new PaymentOptions
        {
            Mode = mode,
            MerchantAccountId = "123456",
            SecretKey = secretKey,
            IsTest = isTest
        }));

    private static PaymentOrder CreateOrder() => new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000011"),
        PurchaseId = Guid.Parse("00000000-0000-0000-0000-000000000022"),
        AmountMinor = 125_050,
        Currency = "RUB",
        Description = "Подписка opencrm.example.invalid",
        ReturnUrl = "https://opencrm.example.invalid/payment/complete",
        ApplicationId = "opencrm",
        MerchantAccountId = "123456",
        BuyerId = "buyer-1",
        BeneficiaryId = "buyer-1",
        IsTest = true
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? RequestBody { get; private set; }
        public string? IdempotenceKey { get; private set; }
        public string? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.Method = request.Method;
            this.RequestUri = request.RequestUri;
            this.RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            this.IdempotenceKey = request.Headers.TryGetValues("Idempotence-Key", out var keys)
                ? keys.SingleOrDefault()
                : null;
            this.Authorization = request.Headers.Authorization?.ToString();

            return handler(request);
        }
    }

    private sealed class NeverCompletingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            return 0;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
    }

    private sealed class PartialThenFailingStream : Stream
    {
        private bool _sentPartialBody;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!this._sentPartialBody)
            {
                this._sentPartialBody = true;
                "{"u8.CopyTo(buffer.Span);

                return ValueTask.FromResult(1);
            }

            return ValueTask.FromException<int>(new IOException("Тестовый сбой чтения тела ответа."));
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (!this._sentPartialBody)
            {
                this._sentPartialBody = true;
                buffer[offset] = (byte)'{';

                return Task.FromResult(1);
            }

            return Task.FromException<int>(new IOException("Тестовый сбой чтения тела ответа."));
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
    }
}
