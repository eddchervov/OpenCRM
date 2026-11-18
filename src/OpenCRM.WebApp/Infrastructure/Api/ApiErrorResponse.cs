namespace OpenCRM.WebApp.Infrastructure.Api;

using System.Globalization;
using System.Resources;
using OpenCRM.WebApp.Infrastructure.Localization;

/// <summary> DTO стандартного ответа об ошибке API. </summary>
public sealed class ApiErrorResponse
{
    private static readonly ResourceManager s_resources = new(
        "OpenCRM.WebApp.Resources.Infrastructure.Localization.LocalizationStrings",
        typeof(LocalizationStrings).Assembly);

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
        string? traceId,
        string? cultureName = null)
    {
        var culture = cultureName is null ? CultureInfo.CurrentUICulture : CultureInfo.GetCultureInfo(cultureName);

        return new ApiErrorResponse
        {
            Success = false,
            ErrorCode = ResolveErrorCode(statusCode),
            Message = string.IsNullOrWhiteSpace(message)
                ? ResolveDefaultMessage(statusCode, cultureName)
                : ApiErrorResponse.s_resources.GetString(message.Trim(), culture) ?? message.Trim(),
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
    public static string ResolveDefaultMessage(int statusCode, string? cultureName = null)
    {
        var culture = cultureName is null ? CultureInfo.CurrentUICulture : CultureInfo.GetCultureInfo(cultureName);

        var resourceKey = statusCode switch
        {
            StatusCodes.Status400BadRequest => "api.badRequest",
            StatusCodes.Status401Unauthorized => "api.unauthorized",
            StatusCodes.Status402PaymentRequired => "api.paymentRequired",
            StatusCodes.Status403Forbidden => "api.forbidden",
            StatusCodes.Status404NotFound => "api.notFound",
            StatusCodes.Status409Conflict => "api.conflict",
            StatusCodes.Status422UnprocessableEntity => "api.validationError",
            _ when statusCode >= 500 => "api.serverError",
            _ => "api.requestError"
        };

        return ApiErrorResponse.s_resources.GetString(resourceKey, culture)!;
    }
}
