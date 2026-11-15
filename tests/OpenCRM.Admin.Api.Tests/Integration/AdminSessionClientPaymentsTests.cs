using OpenCRM.Admin.Api.Models;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OpenCRM.Admin.Api;

namespace OpenCRM.Admin.Api.Tests.Integration;

public sealed class AdminSessionClientPaymentsTests
{
    [Fact]
    public async Task BillingOperationsUseExpectedRoutesMethodsAndRequestBodies()
    {
        var requests = new List<(string Method, string Path, string? Body)>();
        using var client = CreateClient(async (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login") return JsonResponse(HttpStatusCode.OK, CreateTokens());
            if (path == "/api/v1/admin/current-user") return JsonResponse(HttpStatusCode.OK, CreateProfile());

            requests.Add((request.Method.Method, path, request.Content == null
                ? null
                : await request.Content.ReadAsStringAsync()));
            return path switch
            {
                "/api/v1/admin/billing/purchases" => JsonResponse(HttpStatusCode.OK, new[]
                {
                    new { paymentId = 42, buyerType = "individual", npdReceiptIssued = true }
                }),
                _ => JsonResponse(HttpStatusCode.OK, new { message = "Операция выполнена.", status = "completed" })
            };
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password");

        var purchases = await session.GetBillingPurchasesAsync();
        await session.SyncBillingPaymentAsync(42);
        await session.MarkBillingPaymentStatusAsync(42, "completed", "pending", "Проверено вручную.");
        await session.MarkNpdReceiptAsync(42, true, false, "Чек выдан.");
        await session.UpdateNpdReceiptDetailsAsync(42, "https://nalog.ru/receipt", "Чек сформирован.");
        await session.DeleteNpdReceiptFileAsync(42);
        await session.SendNpdReceiptAsync(42);

        Assert.Single(purchases);
        Assert.Equal(42, purchases[0].PaymentId);
        Assert.True(purchases[0].NpdReceiptIssued);
        Assert.Equal(
            new[]
            {
                ("GET", "/api/v1/admin/billing/purchases"),
                ("POST", "/api/v1/admin/billing/payments/42/sync-yookassa"),
                ("POST", "/api/v1/admin/billing/payments/42/mark-status"),
                ("PUT", "/api/v1/admin/billing/payments/42/npd-receipt"),
                ("PUT", "/api/v1/admin/billing/payments/42/npd-receipt/details"),
                ("DELETE", "/api/v1/admin/billing/payments/42/npd-receipt/file"),
                ("POST", "/api/v1/admin/billing/payments/42/npd-receipt/send")
            },
            requests.Select(request => (request.Method, request.Path)));
        AssertBodyProperty(requests[2].Body, "status", "completed");
        AssertBodyProperty(requests[2].Body, "expectedCurrentStatus", "pending");
        AssertBodyProperty(requests[2].Body, "reason", "Проверено вручную.");
        AssertBodyProperty(requests[3].Body, "issued", true);
        AssertBodyProperty(requests[3].Body, "expectedIssued", false);
        AssertBodyProperty(requests[3].Body, "reason", "Чек выдан.");
        AssertBodyProperty(requests[4].Body, "receiptUrl", "https://nalog.ru/receipt");
        AssertBodyProperty(requests[4].Body, "comment", "Чек сформирован.");
    }

    [Fact]
    public async Task PaymentServiceActionsReadStateAndAcceptAsynchronousOperations()
    {
        var requests = new List<(string Method, string Path, string? Body)>();
        using var client = CreateClient(async (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login") return JsonResponse(HttpStatusCode.OK, CreateTokens());
            if (path == "/api/v1/admin/current-user") return JsonResponse(HttpStatusCode.OK, CreateProfile());
            requests.Add((request.Method.Method, path, request.Content == null ? null : await request.Content.ReadAsStringAsync()));
            if (request.Method == HttpMethod.Get)
            {
                return JsonResponse(HttpStatusCode.OK, new { stateToken = "state-1", canCheck = true, canRefund = true, refundableAmount = 210m });
            }

            return JsonResponse(HttpStatusCode.Accepted, new
            {
                id = Guid.Parse("22000000-0000-4000-8000-000000000001"), action = "refund", status = "queued",
                message = "Возврат поставлен в очередь.", amount = 210m, reason = "Проверенный возврат"
            });
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password");

        var state = await session.GetBillingPaymentServiceActionsAsync(42);
        var operationId = Guid.Parse("22000000-0000-4000-8000-000000000001");
        var result = await session.ExecuteBillingPaymentServiceActionAsync(42,
            new AdminBillingPaymentServiceActionRequest(operationId, "refund", "Проверенный возврат", 210m, "state-1"));

        Assert.True(state.CanRefund);
        Assert.Equal(210m, state.RefundableAmount);
        Assert.Equal(operationId, result.Id);
        Assert.Equal("queued", result.Status);
        Assert.Equal(("GET", "/api/v1/admin/billing/payments/42/service-actions"), (requests[0].Method, requests[0].Path));
        Assert.Equal(("POST", "/api/v1/admin/billing/payments/42/service-actions"), (requests[1].Method, requests[1].Path));
        AssertBodyProperty(requests[1].Body, "operationId", operationId.ToString());
        AssertBodyProperty(requests[1].Body, "action", "refund");
        AssertBodyProperty(requests[1].Body, "stateToken", "state-1");
        AssertBodyProperty(requests[1].Body, "amount", 210m);
    }

    [Fact]
    public async Task UploadAfterUnauthorizedRecreatesMultipartBodyWithSameFile()
    {
        var uploadBodies = new List<string>();
        var uploadTokens = new List<string?>();
        using var client = CreateClient(async (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login") return JsonResponse(HttpStatusCode.OK, CreateTokens());
            if (path == "/api/v1/admin/current-user") return JsonResponse(HttpStatusCode.OK, CreateProfile());
            if (path == "/api/v1/auth/refresh") return JsonResponse(HttpStatusCode.OK, CreateTokens("new-access", "new-refresh"));

            Assert.Equal("/api/v1/admin/billing/payments/42/npd-receipt/file", path);
            uploadTokens.Add(request.Headers.Authorization?.Parameter);
            uploadBodies.Add(await request.Content!.ReadAsStringAsync());
            return uploadBodies.Count == 1
                ? JsonResponse(HttpStatusCode.Unauthorized, new { message = "Токен истек." })
                : JsonResponse(HttpStatusCode.OK, new { message = "Файл загружен." });
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password");
        var bytes = "%PDF-test"u8.ToArray();
        await using var input = new MemoryStream(bytes);

        var result = await session.UploadNpdReceiptFileAsync(42, input, "receipt.pdf", "application/pdf");

        Assert.Equal("Файл загружен.", result.Message);
        Assert.Equal(new[] { "access-token", "new-access" }, uploadTokens);
        Assert.Equal(uploadBodies[0].Split("\r\n")[1..^2], uploadBodies[1].Split("\r\n")[1..^2]);
        Assert.Contains("name=file; filename=receipt.pdf", uploadBodies[0]);
        Assert.Contains("Content-Type: application/pdf", uploadBodies[0]);
        Assert.Contains("%PDF-test", uploadBodies[0]);
    }

    [Fact]
    public async Task DownloadAfterUnauthorizedReturnsFileContentAndHeaders()
    {
        var downloadTokens = new List<string?>();
        using var client = CreateClient((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login") return JsonResponse(HttpStatusCode.OK, CreateTokens());
            if (path == "/api/v1/admin/current-user") return JsonResponse(HttpStatusCode.OK, CreateProfile());
            if (path == "/api/v1/auth/refresh") return JsonResponse(HttpStatusCode.OK, CreateTokens("new-access", "new-refresh"));

            Assert.Equal("/api/v1/admin/billing/payments/42/npd-receipt/file", path);
            downloadTokens.Add(request.Headers.Authorization?.Parameter);
            if (downloadTokens.Count == 1) return JsonResponse(HttpStatusCode.Unauthorized, new { message = "Токен истек." });

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent("%PDF-file"u8.ToArray())
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            response.Content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment")
            {
                FileName = "receipt.pdf"
            };
            return response;
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password");

        var file = await session.DownloadNpdReceiptFileAsync(42);

        Assert.Equal(new[] { "access-token", "new-access" }, downloadTokens);
        Assert.Equal("receipt.pdf", file.FileName);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("%PDF-file"u8.ToArray(), file.Content);
    }

    [Fact]
    public async Task ForbiddenBillingMutationReturnsApiErrorWithoutRetry()
    {
        var calls = 0;
        using var client = CreateClient((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login") return JsonResponse(HttpStatusCode.OK, CreateTokens());
            if (path == "/api/v1/admin/current-user") return JsonResponse(HttpStatusCode.OK, CreateProfile());
            calls++;
            return JsonResponse(HttpStatusCode.Forbidden, new { message = "Нет права управлять платежами." });
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password");

        var exception = await Assert.ThrowsAsync<ApiException>(() => session.SendNpdReceiptAsync(42));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Equal("Нет права управлять платежами.", exception.Message);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task MalformedPurchaseResponseBecomesApiError()
    {
        using var client = CreateClient((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/auth/login" => JsonResponse(HttpStatusCode.OK, CreateTokens()),
            "/api/v1/admin/current-user" => JsonResponse(HttpStatusCode.OK, CreateProfile()),
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not-json") }
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password");

        var exception = await Assert.ThrowsAsync<ApiException>(() => session.GetBillingPurchasesAsync());

        Assert.Contains("некорректный ответ", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancelledUploadRequestPreservesCancellation()
    {
        using var client = CreateClient((request, cancellationToken) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login") return JsonResponse(HttpStatusCode.OK, CreateTokens());
            if (path == "/api/v1/admin/current-user") return JsonResponse(HttpStatusCode.OK, CreateProfile());
            throw new OperationCanceledException(cancellationToken);
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await using var input = new MemoryStream("%PDF-test"u8.ToArray());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            session.UploadNpdReceiptFileAsync(42, input, "receipt.pdf", "application/pdf", cancellation.Token));
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        return new HttpClient(new FakeHttpMessageHandler(handler)) { BaseAddress = new Uri("https://opencrm.test/") };
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
    {
        return CreateClient((request, cancellationToken) => Task.FromResult(handler(request, cancellationToken)));
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object value)
    {
        return new HttpResponseMessage(statusCode) { Content = JsonContent.Create(value) };
    }

    private static void AssertBodyProperty(string? body, string propertyName, string expectedValue)
    {
        using var document = JsonDocument.Parse(body!);
        Assert.Equal(expectedValue, document.RootElement.GetProperty(propertyName).GetString());
    }

    private static void AssertBodyProperty(string? body, string propertyName, bool expectedValue)
    {
        using var document = JsonDocument.Parse(body!);
        Assert.Equal(expectedValue, document.RootElement.GetProperty(propertyName).GetBoolean());
    }

    private static void AssertBodyProperty(string? body, string propertyName, decimal expectedValue)
    {
        using var document = JsonDocument.Parse(body!);
        Assert.Equal(expectedValue, document.RootElement.GetProperty(propertyName).GetDecimal());
    }

    private static object CreateTokens(string accessToken = "access-token", string refreshToken = "refresh-token")
    {
        var now = DateTimeOffset.UtcNow;
        return new
        {
            accessToken,
            accessTokenExpiresAt = now.AddHours(1),
            refreshToken,
            refreshTokenExpiresAt = now.AddDays(10),
            tokenType = "Bearer",
            user = new { id = "admin-1", email = "contact@example.invalid", userName ="admin", roles = new[] { "Admin" } }
        };
    }

    private static object CreateProfile()
    {
        return new { id = "admin-1", email = "contact@example.invalid", roles = new[] { "Admin" }, permissions = new[] { "Admin.Access" } };
    }

    private sealed class FakeHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return handler(request, cancellationToken);
        }
    }
}
