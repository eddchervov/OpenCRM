using System.Net;
using System.Net.Http.Headers;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.Api;

public sealed partial class AdminSessionClient
{
    /// <summary> Получает список покупок для очереди чеков НПД. </summary>
    public Task<IReadOnlyList<AdminBillingPurchase>> GetBillingPurchasesAsync(
        CancellationToken cancellationToken = default)
    {
        return this.GetAuthorizedReportAsync<IReadOnlyList<AdminBillingPurchase>>(
            "api/v1/admin/billing/purchases",
            "Не удалось загрузить список покупок.",
            cancellationToken);
    }

    /// <summary> Синхронизирует статус платежа с YooKassa. </summary>
    public Task<AdminBillingOperationResponse> SyncBillingPaymentAsync(
        int paymentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(paymentId);

        return this.SendAuthorizedRequestAsync<AdminBillingOperationResponse>(
            HttpMethod.Post,
            $"api/v1/admin/billing/payments/{paymentId}/sync-yookassa",
            null,
            "Не удалось синхронизировать платеж с YooKassa.",
            cancellationToken);
    }

    /// <summary> Получает доступные операции и журнал действий платёжного сервиса. </summary>
    public Task<AdminBillingPaymentServiceActions> GetBillingPaymentServiceActionsAsync(
        int paymentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(paymentId);

        return this.GetAuthorizedReportAsync<AdminBillingPaymentServiceActions>(
            $"api/v1/admin/billing/payments/{paymentId}/service-actions",
            "Не удалось загрузить действия по платежу.",
            cancellationToken);
    }

    /// <summary> Выполняет проверку, возврат, повтор выдачи доступа или завершает разбор. </summary>
    public Task<AdminBillingPaymentServiceActionOperation> ExecuteBillingPaymentServiceActionAsync(
        int paymentId,
        AdminBillingPaymentServiceActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(paymentId);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfEqual(request.OperationId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Action);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.StateToken);

        return this.SendAuthorizedRequestAsync<AdminBillingPaymentServiceActionOperation>(
            HttpMethod.Post,
            $"api/v1/admin/billing/payments/{paymentId}/service-actions",
            request,
            "Не удалось выполнить действие по платежу.",
            cancellationToken);
    }

    /// <summary> Устанавливает статус платежа с проверкой ожидаемого текущего статуса. </summary>
    public Task<AdminBillingOperationResponse> MarkBillingPaymentStatusAsync(
        int paymentId,
        string? status,
        string? expectedCurrentStatus,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(paymentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return this.SendAuthorizedRequestAsync<AdminBillingOperationResponse>(
            HttpMethod.Post,
            $"api/v1/admin/billing/payments/{paymentId}/mark-status",
            new MarkBillingPaymentStatusRequest(status, expectedCurrentStatus, reason),
            "Не удалось изменить статус платежа.",
            cancellationToken);
    }

    /// <summary> Отмечает выдачу чека НПД с проверкой ожидаемого состояния. </summary>
    public Task<AdminOperationResponse> MarkNpdReceiptAsync(
        int paymentId,
        bool issued,
        bool expectedIssued,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(paymentId);

        return this.SendAuthorizedRequestAsync<AdminOperationResponse>(
            HttpMethod.Put,
            $"api/v1/admin/billing/payments/{paymentId}/npd-receipt",
            new MarkNpdReceiptRequest(issued, expectedIssued, reason),
            "Не удалось изменить отметку о чеке.",
            cancellationToken);
    }

    /// <summary> Сохраняет ссылку и комментарий к чеку НПД. </summary>
    public Task<AdminOperationResponse> UpdateNpdReceiptDetailsAsync(
        int paymentId,
        string? receiptUrl,
        string? comment,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(paymentId);

        return this.SendAuthorizedRequestAsync<AdminOperationResponse>(
            HttpMethod.Put,
            $"api/v1/admin/billing/payments/{paymentId}/npd-receipt/details",
            new UpdateNpdReceiptDetailsRequest(receiptUrl, comment),
            "Не удалось сохранить данные чека.",
            cancellationToken);
    }

    /// <summary> Загружает файл чека НПД. </summary>
    public async Task<AdminOperationResponse> UploadNpdReceiptFileAsync(
        int paymentId,
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(paymentId);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        var fileBytes = await this.ReadReceiptFileAsync(content, cancellationToken);
        if (fileBytes.Length == 0)
        {
            throw new ArgumentException(this.LocalizeFallback("Файл чека не может быть пустым."), nameof(content));
        }

        return await this.SendAuthorizedContentRequestAsync<AdminOperationResponse>(
            HttpMethod.Post,
            $"api/v1/admin/billing/payments/{paymentId}/npd-receipt/file",
            () => AdminSessionClient.CreateReceiptMultipartContent(fileBytes, fileName, contentType),
            "Не удалось загрузить файл чека.",
            cancellationToken);
    }

    /// <summary> Скачивает файл чека НПД. </summary>
    public async Task<AdminNpdReceiptFile> DownloadNpdReceiptFileAsync(
        int paymentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(paymentId);
        this.ThrowIfDisposed();

        var path = $"api/v1/admin/billing/payments/{paymentId}/npd-receipt/file";
        var requestSession = await this.GetReportRequestSessionAsync(cancellationToken);
        using var response = await this.SendAsync(
            HttpMethod.Get,
            path,
            null,
            requestSession.Tokens.AccessToken,
            cancellationToken);

        var requestTokens = requestSession.Tokens;
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            requestTokens = await this.RefreshAfterUnauthorizedAsync(requestSession, requestTokens, cancellationToken);
            using var retryResponse = await this.SendAsync(
                HttpMethod.Get,
                path,
                null,
                requestTokens.AccessToken,
                cancellationToken);

            return await this.CompleteAuthorizedFileRequestAsync(
                retryResponse,
                requestSession.Version,
                requestTokens,
                cancellationToken);
        }

        return await this.CompleteAuthorizedFileRequestAsync(
            response,
            requestSession.Version,
            requestTokens,
            cancellationToken);
    }

    /// <summary> Удаляет файл чека НПД. </summary>
    public Task<AdminOperationResponse> DeleteNpdReceiptFileAsync(
        int paymentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(paymentId);

        return this.SendAuthorizedRequestAsync<AdminOperationResponse>(
            HttpMethod.Delete,
            $"api/v1/admin/billing/payments/{paymentId}/npd-receipt/file",
            null,
            "Не удалось удалить файл чека.",
            cancellationToken);
    }

    /// <summary> Отправляет чек НПД покупателю. </summary>
    public Task<AdminOperationResponse> SendNpdReceiptAsync(
        int paymentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(paymentId);

        return this.SendAuthorizedRequestAsync<AdminOperationResponse>(
            HttpMethod.Post,
            $"api/v1/admin/billing/payments/{paymentId}/npd-receipt/send",
            null,
            "Не удалось поставить чек в очередь отправки.",
            cancellationToken);
    }

    private async Task<TResponse> SendAuthorizedContentRequestAsync<TResponse>(
        HttpMethod method,
        string path,
        Func<HttpContent?> contentFactory,
        string fallbackMessage,
        CancellationToken cancellationToken)
    {
        this.ThrowIfDisposed();
        var requestSession = await this.GetReportRequestSessionAsync(cancellationToken);
        using var response = await this.SendAsync(
            method,
            path,
            contentFactory(),
            requestSession.Tokens.AccessToken,
            cancellationToken);

        var requestTokens = requestSession.Tokens;
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            requestTokens = await this.RefreshAfterUnauthorizedAsync(requestSession, requestTokens, cancellationToken);
            using var retryResponse = await this.SendAsync(
                method,
                path,
                contentFactory(),
                requestTokens.AccessToken,
                cancellationToken);

            return await this.CompleteAuthorizedReportRequestAsync<TResponse>(
                retryResponse,
                fallbackMessage,
                requestSession.Version,
                requestTokens,
                cancellationToken);
        }

        return await this.CompleteAuthorizedReportRequestAsync<TResponse>(
            response,
            fallbackMessage,
            requestSession.Version,
            requestTokens,
            cancellationToken);
    }

    private async Task<AdminNpdReceiptFile> CompleteAuthorizedFileRequestAsync(
        HttpResponseMessage response,
        long sessionVersion,
        ApiTokenResponse requestTokens,
        CancellationToken cancellationToken)
    {
        await this._sessionLock.WaitAsync(cancellationToken);

        try
        {
            this.EnsureCurrentReportSession(sessionVersion);
            if (response.StatusCode == HttpStatusCode.Unauthorized && ReferenceEquals(this._tokens, requestTokens))
            {
                this.ClearSession();
            }

            if (!response.IsSuccessStatusCode)
            {
                throw await this.CreateApiExceptionAsync(response, "Не удалось скачать файл чека.", cancellationToken);
            }

            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName
                ?? this.LocalizeFallback("чек-нпд");

            return new AdminNpdReceiptFile(
                await response.Content.ReadAsByteArrayAsync(cancellationToken),
                response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream",
                fileName.Trim('"'));
        }
        finally
        {
            this._sessionLock.Release();
        }
    }

    private async Task<byte[]> ReadReceiptFileAsync(Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var bytesRead = await content.ReadAsync(chunk, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            if (buffer.Length + bytesRead > 5_242_880)
            {
                throw new ArgumentException(this.LocalizeFallback("Размер файла должен быть не больше 5 МБ."), nameof(content));
            }

            await buffer.WriteAsync(chunk.AsMemory(0, bytesRead), cancellationToken);
        }

        return buffer.ToArray();
    }

    private static MultipartFormDataContent CreateReceiptMultipartContent(
        byte[] fileBytes,
        string fileName,
        string contentType)
    {
        var multipartContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        multipartContent.Add(fileContent, "file", fileName);

        return multipartContent;
    }

    private sealed record MarkBillingPaymentStatusRequest(
        string? Status,
        string? ExpectedCurrentStatus,
        string Reason);

    private sealed record MarkNpdReceiptRequest(bool Issued, bool ExpectedIssued, string? Reason);

    private sealed record UpdateNpdReceiptDetailsRequest(string? ReceiptUrl, string? Comment);
}
