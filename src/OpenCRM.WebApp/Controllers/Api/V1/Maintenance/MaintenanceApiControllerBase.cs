using Microsoft.AspNetCore.Mvc;
using OpenCRM.WebApp.Controllers.Api.V1;

namespace OpenCRM.WebApp.Controllers.Api.V1.Maintenance;

/// <summary> Базовый класс контроллеров обслуживания. </summary>
public abstract class MaintenanceApiControllerBase : Controller
{
    /// <summary> Определяет, является ли исключение ошибкой доступа. </summary>
    protected static bool IsAccessDeniedError(Exception exception)
    {
        return exception.Message.Contains("нет доступа", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("только на просмотр", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("только для чтения", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary> Определяет, является ли исключение ошибкой отсутствия данных. </summary>
    protected static bool IsNotFoundError(Exception exception)
    {
        return exception.Message.Contains("не найден", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("not found", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary> Возвращает идентификатор текущего пользователя или ошибку. </summary>
    protected ActionResult<string> GetCurrentUserId()
    {
        var userId = this.GetApiUserId();
        return string.IsNullOrWhiteSpace(userId) ? Unauthorized() : userId;
    }
}
