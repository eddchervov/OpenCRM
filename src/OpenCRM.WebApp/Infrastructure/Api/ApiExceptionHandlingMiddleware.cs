using System.Text.Json;

namespace OpenCRM.WebApp.Infrastructure.Api;

/// <summary> Промежуточное ПО, обрабатывающее необработанные исключения и возвращающее единый ответ об ошибке API. </summary>
public sealed class ApiExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ApiExceptionHandlingMiddleware> logger)
{
    /// <summary> Перехватывает исключения конвейера и пишет ошибку в ответ. </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        try
        {
            await next(context);

            if (!context.Response.HasStarted
                && context.Response.StatusCode >= StatusCodes.Status400BadRequest
                && context.Response.ContentLength is null
                && string.IsNullOrWhiteSpace(context.Response.ContentType))
            {
                await WriteErrorAsync(context, context.Response.StatusCode, null, null);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "API request forbidden: {Path}", context.Request.Path);
            await WriteErrorAsync(context, StatusCodes.Status403Forbidden, ex.Message, null);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "API request rejected: {Path}", context.Request.Path);
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest, ex.Message, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled API exception: {Path}", context.Request.Path);
            await WriteErrorAsync(context, StatusCodes.Status500InternalServerError, null, null);
        }
    }

    /// <summary> Записывает JSON-ответ об ошибке с заданным кодом состояния. </summary>
    private static async Task WriteErrorAsync(
        HttpContext context,
        int statusCode,
        string? message,
        IReadOnlyDictionary<string, string[]>? errors)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        var response = ApiErrorResponse.Create(statusCode, message, errors, context.TraceIdentifier);
        await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }
}
