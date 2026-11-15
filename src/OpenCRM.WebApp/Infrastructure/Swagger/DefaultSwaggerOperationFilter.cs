using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace OpenCRM.WebApp.Infrastructure.Swagger;

/// <summary> Фильтр, задающий стандартные описания операций в Swagger. </summary>
public sealed class DefaultSwaggerOperationFilter : IOperationFilter
{
    /// <summary> Заполняет summary и описание операции Swagger. </summary>
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        operation.Summary ??= BuildSummary(context);
        operation.Description ??= BuildDescription(context);

        if (IsAnonymous(context))
        {
            return;
        }

        operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Требуется авторизация." });
        operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Недостаточно прав для выполнения операции." });
        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                []
            }
        });

        if (IsUnsafeHttpMethod(context.ApiDescription.HttpMethod))
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "RequestVerificationToken",
                In = ParameterLocation.Header,
                Required = false,
                Description = "Antiforgery token для cookie-сессии WebApp. Для JWT Bearer-запросов мобильного/API клиента не нужен.",
                Schema = new OpenApiSchema { Type = "string" }
            });
        }
    }

    /// <summary> Формирует краткое описание операции. </summary>
    private static string BuildSummary(OperationFilterContext context)
    {
        var httpMethod = context.ApiDescription.HttpMethod ?? "HTTP";
        var routeValues = context.ApiDescription.ActionDescriptor.RouteValues;
        var controller = routeValues.TryGetValue("controller", out var controllerName) ? controllerName ?? "API" : "API";
        var action = routeValues.TryGetValue("action", out var actionName) ? actionName ?? "method" : "method";
        return $"{httpMethod.ToUpperInvariant()} {controller}.{action}";
    }

    /// <summary> Формирует подробное описание операции. </summary>
    private static string BuildDescription(OperationFilterContext context)
    {
        var relativePath = context.ApiDescription.RelativePath ?? string.Empty;
        return $"Метод API `{relativePath}`. Используется WebApp, мобильным приложением или внешним клиентом.";
    }

    /// <summary> Определяет, доступна ли операция анонимно. </summary>
    private static bool IsAnonymous(OperationFilterContext context)
    {
        return context.MethodInfo.GetCustomAttributes(true).OfType<IAllowAnonymous>().Any()
            || context.MethodInfo.DeclaringType?.GetCustomAttributes(true).OfType<IAllowAnonymous>().Any() == true;
    }

    /// <summary> Проверяет, что HTTP-метод изменяет данные. </summary>
    private static bool IsUnsafeHttpMethod(string? httpMethod)
    {
        return !string.Equals(httpMethod, HttpMethods.Get, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(httpMethod, HttpMethods.Head, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(httpMethod, HttpMethods.Options, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(httpMethod, HttpMethods.Trace, StringComparison.OrdinalIgnoreCase);
    }
}
