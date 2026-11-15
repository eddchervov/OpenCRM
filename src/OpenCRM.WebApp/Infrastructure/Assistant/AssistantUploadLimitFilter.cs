using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace OpenCRM.WebApp.Infrastructure.Assistant;

public sealed class AssistantUploadLimitFilter(AssistantOptions options) : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        if (context.HttpContext.Request.ContentLength > options.MaximumUploadRequestBytes)
        {
            context.Result = new StatusCodeResult(StatusCodes.Status413PayloadTooLarge);

            return;
        }

        var feature = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false })
        {
            feature.MaxRequestBodySize = options.MaximumUploadRequestBytes;
        }

        await next();
    }
}
