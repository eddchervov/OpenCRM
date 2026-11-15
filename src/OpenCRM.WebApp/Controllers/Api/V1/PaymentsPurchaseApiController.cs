using OpenCRM.WebApp.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.WebApp.Infrastructure.Api;

namespace OpenCRM.WebApp.Controllers.Api.V1;

/// <summary> API состояния покупок подписки. </summary>
[ApiController]
[Authorize]
[Route("api/v1/payments/purchases")]
[CommercialModule]
public sealed class PaymentsPurchaseApiController(
    IPaymentPurchaseQueryService purchaseQueryService) : ControllerBase
{
    /// <summary> Возвращает состояние покупки только её владельцу. </summary>
    [HttpGet("{purchaseId:guid}")]
    public async Task<IActionResult> Get(Guid purchaseId, CancellationToken cancellationToken)
    {
        var userId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var state = await purchaseQueryService.GetAsync(userId, purchaseId, cancellationToken);
        if (state == null)
        {
            return NotFound();
        }

        return CheckoutHttpResponse.CreatePurchaseState(state, purchaseId);
    }
}
