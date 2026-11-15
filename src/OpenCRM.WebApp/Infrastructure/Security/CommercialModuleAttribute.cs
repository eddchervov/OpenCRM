using Microsoft.AspNetCore.Mvc;

namespace OpenCRM.WebApp.Infrastructure.Security;

/// <summary> Делает маршрут коммерческого модуля недоступным, пока модуль выключен. </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class CommercialModuleAttribute() : TypeFilterAttribute(typeof(CommercialModuleFilter))
{
}
