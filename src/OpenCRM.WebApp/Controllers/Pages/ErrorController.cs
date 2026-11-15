using OpenCRM.WebApp.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;

namespace OpenCRM.WebApp.Controllers.Pages
{
    /// <summary> Контроллер отображения страниц ошибок. </summary>
    [AllowUnconfirmedEmail]
    public class ErrorController : Controller
    {
        /// <summary> Возвращает страницу ошибки в зависимости от кода состояния. </summary>
        [Route("Error/{statusCode?}")]
        public IActionResult HandleError(int? statusCode = null)
        {
            var effectiveStatusCode = statusCode is >= 400 and <= 599
                ? statusCode.Value
                : StatusCodes.Status404NotFound;

            Response.StatusCode = effectiveStatusCode;

            return effectiveStatusCode switch
            {
                StatusCodes.Status404NotFound => View("NotFound"),
                _ => View("Error")
            };
        }
    }
}

