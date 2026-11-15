using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace OpenCRM.WebApp.Infrastructure.Api;

/// <summary> Фильтр, преобразующий результаты в стандартный ответ об ошибке API. </summary>
public sealed class ApiErrorResultFilter : IResultFilter
{
    /// <summary> Логгер для записи ошибок при обработке результатов API. </summary>
    private readonly ILogger<ApiErrorResultFilter> _logger;

    /// <summary> Создаёт фильтр с логгером. </summary>
    public ApiErrorResultFilter(ILogger<ApiErrorResultFilter> logger)
    {
        this._logger = logger;
    }

    /// <summary> Нормализует ответы с ошибками перед отправкой результата клиенту. </summary>
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (!IsApiRequest(context.HttpContext) || context.Result is FileResult)
        {
            return;
        }

        switch (context.Result)
        {
            case ObjectResult objectResult when IsErrorStatus(objectResult.StatusCode):
                this.LogValidationFailure(context.HttpContext, objectResult.Value);
                objectResult.Value = NormalizeObjectResult(context.HttpContext, objectResult);
                objectResult.DeclaredType = typeof(ApiErrorResponse);
                break;

            case JsonResult jsonResult when IsErrorStatus(jsonResult.StatusCode):
                jsonResult.Value = ApiErrorResponse.Create(
                    jsonResult.StatusCode!.Value,
                    ExtractMessage(jsonResult.Value),
                    ExtractErrors(jsonResult.Value),
                    context.HttpContext.TraceIdentifier);
                break;

            case StatusCodeResult statusCodeResult when IsErrorStatus(statusCodeResult.StatusCode):
                context.Result = new ObjectResult(ApiErrorResponse.Create(
                    statusCodeResult.StatusCode,
                    null,
                    null,
                    context.HttpContext.TraceIdentifier))
                {
                    StatusCode = statusCodeResult.StatusCode,
                    DeclaredType = typeof(ApiErrorResponse)
                };
                break;
        }
    }

    /// <summary> Пустая реализация пост-обработки результата. </summary>
    public void OnResultExecuted(ResultExecutedContext context)
    {
    }

    /// <summary> Логирует ошибку валидации модели из контекста запроса. </summary>
    private void LogValidationFailure(HttpContext httpContext, object? value)
    {
        Dictionary<string, string[]>? errors = value switch
        {
            ValidationProblemDetails validationProblem => validationProblem.Errors.ToDictionary(error => error.Key, error => error.Value),
            SerializableError serializableError => ExtractSerializableErrors(serializableError).ToDictionary(error => error.Key, error => error.Value),
            _ => null
        };

        if (errors == null || errors.Count == 0)
        {
            return;
        }

        this._logger.LogWarning(
            "API request validation failed. Method: {RequestMethod}; Path: {RequestPath}; TraceId: {TraceId}; Errors: {@ValidationErrors}",
            httpContext.Request.Method,
            httpContext.Request.Path.Value,
            httpContext.TraceIdentifier,
            errors);
    }

    /// <summary> Приводит ObjectResult к единому формату ответа об ошибке API. </summary>
    private static ApiErrorResponse NormalizeObjectResult(HttpContext httpContext, ObjectResult result)
    {
        var statusCode = result.StatusCode ?? StatusCodes.Status500InternalServerError;
        if (result.Value is ApiErrorResponse response)
        {
            return response;
        }

        if (result.Value is ValidationProblemDetails validationProblem)
        {
            return ApiErrorResponse.Create(
                statusCode,
                "Проверьте данные формы.",
                validationProblem.Errors.ToDictionary(x => x.Key, x => x.Value),
                httpContext.TraceIdentifier);
        }

        if (result.Value is SerializableError serializableError)
        {
            return ApiErrorResponse.Create(statusCode, null, ExtractSerializableErrors(serializableError), httpContext.TraceIdentifier);
        }

        if (result.Value is ProblemDetails problem)
        {
            return ApiErrorResponse.Create(statusCode, problem.Detail ?? problem.Title, null, httpContext.TraceIdentifier);
        }

        return ApiErrorResponse.Create(statusCode, ExtractMessage(result.Value), ExtractErrors(result.Value), httpContext.TraceIdentifier);
    }

    /// <summary> Извлекает сообщение об ошибке из значения результата. </summary>
    private static string? ExtractMessage(object? value)
    {
        if (value == null)
        {
            return null;
        }

        if (value is string message)
        {
            return message;
        }

        var type = value.GetType();
        var property = type.GetProperty("message") ?? type.GetProperty("Message") ?? type.GetProperty("error") ?? type.GetProperty("Error");
        return property?.GetValue(value)?.ToString();
    }

    /// <summary> Извлекает словарь ошибок валидации из значения результата. </summary>
    private static IReadOnlyDictionary<string, string[]>? ExtractErrors(object? value)
    {
        if (value == null)
        {
            return null;
        }

        var type = value.GetType();
        var property = type.GetProperty("errors") ?? type.GetProperty("Errors");
        var rawErrors = property?.GetValue(value);
        return rawErrors as IReadOnlyDictionary<string, string[]> ?? rawErrors as Dictionary<string, string[]>;
    }

    /// <summary> Преобразует SerializableError в словарь ошибок. </summary>
    private static IReadOnlyDictionary<string, string[]> ExtractSerializableErrors(SerializableError serializableError)
    {
        return serializableError.ToDictionary(
            x => x.Key,
            x => x.Value switch
            {
                string message => [message],
                IEnumerable<string> messages => messages.ToArray(),
                IEnumerable<object> objects => objects.Select(item => item?.ToString() ?? string.Empty).Where(item => item.Length > 0).ToArray(),
                { } value => [value.ToString() ?? string.Empty],
                _ => Array.Empty<string>()
            });
    }

    /// <summary> Определяет, является ли запрос обращением к API. </summary>
    private static bool IsApiRequest(HttpContext context)
    {
        return context.Request.Path.StartsWithSegments("/api");
    }

    /// <summary> Проверяет, что код состояния HTTP означает ошибку. </summary>
    private static bool IsErrorStatus(int? statusCode)
    {
        return statusCode >= StatusCodes.Status400BadRequest;
    }
}
