namespace OpenCRM.WebApp.Infrastructure.Api;

/// <summary> DTO стандартного ответа об ошибке API. </summary>
public sealed class ApiErrorResponse
{
    /// <summary> Признак успешного выполнения запроса. </summary>
    public bool Success { get; init; }

    /// <summary> Код ошибки. </summary>
    public string ErrorCode { get; init; } = string.Empty;

    /// <summary> Сообщение об ошибке. </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary> Ошибки по полям запроса. </summary>
    public IReadOnlyDictionary<string, string[]>? Errors { get; init; }

    /// <summary> Идентификатор трассировки запроса. </summary>
    public string? TraceId { get; init; }

    /// <summary> Создаёт ответ об ошибке с заданными параметрами. </summary>
    public static ApiErrorResponse Create(
        int statusCode,
        string? message,
        IReadOnlyDictionary<string, string[]>? errors,
        string? traceId)
    {
        return new ApiErrorResponse
        {
            Success = false,
            ErrorCode = ResolveErrorCode(statusCode),
            Message = string.IsNullOrWhiteSpace(message) ? ResolveDefaultMessage(statusCode) : message.Trim(),
            Errors = errors?.Count > 0 ? errors : null,
            TraceId = traceId
        };
    }

    /// <summary> Возвращает код ошибки по HTTP-статусу. </summary>
    public static string ResolveErrorCode(int statusCode)
    {
        return statusCode switch
        {
            StatusCodes.Status400BadRequest => "bad_request",
            StatusCodes.Status401Unauthorized => "unauthorized",
            StatusCodes.Status402PaymentRequired => "payment_required",
            StatusCodes.Status403Forbidden => "forbidden",
            StatusCodes.Status404NotFound => "not_found",
            StatusCodes.Status409Conflict => "conflict",
            StatusCodes.Status422UnprocessableEntity => "validation_error",
            _ when statusCode >= 500 => "server_error",
            _ => "request_error"
        };
    }

    /// <summary> Возвращает сообщение по умолчанию для HTTP-статуса. </summary>
    public static string ResolveDefaultMessage(int statusCode)
    {
        return statusCode switch
        {
            StatusCodes.Status400BadRequest => "Некорректный запрос.",
            StatusCodes.Status401Unauthorized => "Требуется авторизация.",
            StatusCodes.Status402PaymentRequired => "Для действия нужен активный тариф.",
            StatusCodes.Status403Forbidden => "Недостаточно прав для действия.",
            StatusCodes.Status404NotFound => "Ресурс не найден.",
            StatusCodes.Status409Conflict => "Запрос конфликтует с текущим состоянием ресурса.",
            StatusCodes.Status422UnprocessableEntity => "Проверьте данные формы.",
            _ when statusCode >= 500 => "Внутренняя ошибка сервера.",
            _ => "Ошибка обработки запроса."
        };
    }
}
