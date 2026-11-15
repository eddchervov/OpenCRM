using System.Net;

namespace OpenCRM.Admin.Api;

/// <summary> Исключение с информацией об ошибке обращения к API. </summary>
public sealed class ApiException : Exception
{
    /// <summary> Создаёт исключение с сообщением, необязательным статусом и внутренней ошибкой. </summary>
    public ApiException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null, string? errorCode = null)
        : base(message, innerException)
    {
        this.StatusCode = statusCode;
        this.ErrorCode = errorCode;
    }

    /// <summary> HTTP-статус ответа API, если известен. </summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary> Безопасный код ошибки API, если сервер его вернул. </summary>
    public string? ErrorCode { get; }
}
