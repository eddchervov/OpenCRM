using System.Globalization;
using System.Resources;
using OpenCRM.WebApp.Infrastructure.Localization;
using OpenCRM.WebApp.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.Services.Interfaces.Payments;
using OpenCRM.WebApp.Models.Payments;

namespace OpenCRM.WebApp.Controllers.Payments;

/// <summary> Контроллер обработки результатов оплаты. </summary>
[Route("payment")]
[CommercialModule]
public class PaymentController(
    UserManager<ApplicationUser> userManager,
    IPaymentCompletionService paymentCompletionService,
    IPaymentPurchaseQueryService paymentPurchaseQueryService,
    Helpers.ProjectBrand? brand = null) : Controller
{
    private static readonly ResourceManager s_resources = new(
        "OpenCRM.WebApp.Resources.Infrastructure.Localization.LocalizationStrings",
        typeof(LocalizationStrings).Assembly);

    private static string Localize(string key) =>
        PaymentController.s_resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;


    /// <summary> Отображает страницу завершения оплаты. </summary>
    [HttpGet("complete")]
    public async Task<IActionResult> Complete([FromQuery] Guid? purchaseId)
    {
        var userId = User.Identity?.IsAuthenticated == true ? userManager.GetUserId(User) : null;
        if (purchaseId.HasValue || Request.Query.ContainsKey("purchaseId"))
        {
            var purchaseState = string.IsNullOrWhiteSpace(userId) || !purchaseId.HasValue
                ? null
                : await paymentPurchaseQueryService.GetAsync(userId, purchaseId.Value);
            var matchingPurchaseState = purchaseId.HasValue && purchaseState?.Result.PurchaseId == purchaseId
                ? purchaseState
                : null;
            var statusCode = GetPurchaseStatusCode(matchingPurchaseState);

            return View(new PaymentCompleteViewModel
            {
                IsConfirmed = statusCode == "confirmed",
                IsCanceled = statusCode == "canceled",
                Title = GetPaymentCompleteTitle(statusCode),
                Message = GetPaymentCompleteMessage(statusCode),
                IconCssClass = GetPaymentCompleteIconCssClass(statusCode),
                PrimaryActionUrl = matchingPurchaseState?.PrimaryActionUrl ?? "/subscription",
                PrimaryActionText = GetPaymentCompletePrimaryActionText(matchingPurchaseState?.PrimaryActionUrl),
                PurchaseId = matchingPurchaseState?.Result.PurchaseId,
                PurchaseStatusUrl = matchingPurchaseState != null
                    ? Url.Content($"~/api/v1/payments/purchases/{matchingPurchaseState.Result.PurchaseId:D}")
                    : null,
                ShouldPollPurchase = matchingPurchaseState != null &&
                    (statusCode is "pending" or "paid_waiting_activation")
            });
        }

        var state = await paymentCompletionService.GetCompleteStateAsync(userId);

        return View(new PaymentCompleteViewModel
        {
            IsConfirmed = state.IsConfirmed,
            IsCanceled = state.IsCanceled,
            Title = GetPaymentCompleteTitle(state.StatusCode),
            Message = GetPaymentCompleteMessage(state.StatusCode),
            IconCssClass = GetPaymentCompleteIconCssClass(state.StatusCode),
            PrimaryActionUrl = state.PrimaryActionUrl,
            PrimaryActionText = GetPaymentCompletePrimaryActionText(state.PrimaryActionUrl)
        });
    }

    /// <summary> Возвращает HTML-страницу результата оплаты для мобильного приложения. </summary>
    [HttpGet("mobile-complete")]
    public ContentResult MobileComplete([FromQuery] Guid? purchaseId = null)
    {
        var appUrl = purchaseId.HasValue
            ? $"opencrm://payment/complete?purchaseId={purchaseId.Value:D}"
            : "opencrm://payment/complete";
        var fallbackUrl = Url.Content("~/");
        var brandName = brand?.DisplayName ?? Helpers.ProjectBrand.DefaultName;
        var returningTitle = System.Text.Encodings.Web.HtmlEncoder.Default.Encode(PaymentController.Localize("payment.mobile.returning"));
        var homeLabel = System.Text.Encodings.Web.HtmlEncoder.Default.Encode(string.Format(
            CultureInfo.CurrentCulture, PaymentController.Localize("payment.mobile.home"), brandName));
        var openAppLabel = System.Text.Encodings.Web.HtmlEncoder.Default.Encode(string.Format(
            CultureInfo.CurrentCulture, PaymentController.Localize("payment.mobile.openApp"), brandName));

        return Content($$"""
            <!doctype html>
            <html lang="{{CultureInfo.CurrentUICulture.Name}}">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>{{returningTitle}} — {{System.Text.Encodings.Web.HtmlEncoder.Default.Encode(brand?.DisplayName ?? Helpers.ProjectBrand.DefaultName)}}</title>
              <meta name="robots" content="noindex,nofollow">
              <meta http-equiv="refresh" content="1; url={{appUrl}}">
              <style>
                body { font-family: Arial, sans-serif; margin: 0; min-height: 100vh; display: grid; place-items: center; background: #252525; color: #fff; }
                main { max-width: 520px; padding: 32px 24px; text-align: center; line-height: 1.7; }
                h1 { font: 500 clamp(26px, 5vw, 36px)/1.3 monospace; text-transform: uppercase; }
                .brand { font-size: 24px; color: #fff; text-decoration: none; }
                .brand span { color: #e67337; }
                a { color: #ffad7e; font-weight: 700; text-underline-offset: 4px; }
                a:focus-visible { outline: 3px solid #e67337; outline-offset: 4px; }
              </style>
            </head>
            <body>
              <main>
                <a class="brand" href="{{fallbackUrl}}" aria-label="{{homeLabel}}">{{System.Text.Encodings.Web.HtmlEncoder.Default.Encode(brand?.DisplayName ?? Helpers.ProjectBrand.DefaultName)}}</a>
                <h1>{{returningTitle}}</h1>
                <p>{{System.Text.Encodings.Web.HtmlEncoder.Default.Encode(PaymentController.Localize("payment.mobile.help"))}}</p>
                <p><a href="{{appUrl}}">{{openAppLabel}}</a></p>
                <p><a href="{{fallbackUrl}}">{{System.Text.Encodings.Web.HtmlEncoder.Default.Encode(PaymentController.Localize("payment.mobile.openSite"))}}</a></p>
              </main>
              <script>
                window.location.href = "{{appUrl}}";
              </script>
            </body>
            </html>
            """, "text/html; charset=utf-8");
    }

    /// <summary> Возвращает CSS-класс иконки по статусу оплаты. </summary>
    private static string GetPaymentCompleteIconCssClass(string? statusCode) => statusCode switch
    {
        "confirmed" => "fa-check-circle text-success",
        "canceled" => "fa-times-circle text-danger",
        "refunded" => "fa-reply text-secondary",
        "manual_review" => "fa-exclamation-triangle text-warning",
        "returned" => "fa-reply text-secondary",
        _ => "fa-clock-o text-primary"
    };

    /// <summary> Возвращает заголовок по статусу оплаты. </summary>
    private static string GetPaymentCompleteTitle(string? statusCode) => statusCode switch
    {
        "confirmed" => PaymentController.Localize("clientUi.paymentConfirmed"),
        "canceled" => PaymentController.Localize("clientUi.paymentCanceled"),
        "refunded" => PaymentController.Localize("clientUi.paymentRefunded"),
        "paid_waiting_activation" => PaymentController.Localize("clientUi.paymentReceived"),
        "manual_review" => PaymentController.Localize("clientUi.paymentReview"),
        "purchase_not_found" => PaymentController.Localize("payment.purchaseMissing"),
        "pending" => PaymentController.Localize("clientUi.paymentPending"),
        _ => PaymentController.Localize("payment.returned")
    };

    /// <summary> Возвращает текст сообщения по статусу оплаты. </summary>
    private static string GetPaymentCompleteMessage(string? statusCode) => statusCode switch
    {
        "confirmed" => PaymentController.Localize("clientUi.paymentConfirmedHelp"),
        "canceled" => PaymentController.Localize("clientUi.paymentCanceledHelp"),
        "refunded" => PaymentController.Localize("clientUi.paymentRefundedHelp"),
        "paid_waiting_activation" => PaymentController.Localize("clientUi.paymentReceivedHelp"),
        "manual_review" => PaymentController.Localize("clientUi.paymentReviewHelp"),
        "purchase_not_found" => PaymentController.Localize("payment.purchaseMissingHelp"),
        "pending" => PaymentController.Localize("clientUi.paymentPendingHelp"),
        _ => PaymentController.Localize("payment.returnedHelp")
    };

    /// <summary> Возвращает текст основного действия по его адресу. </summary>
    private static string GetPaymentCompletePrimaryActionText(string? primaryActionUrl) =>
        !string.IsNullOrWhiteSpace(primaryActionUrl) &&
        (primaryActionUrl.StartsWith("/organizations", StringComparison.OrdinalIgnoreCase) ||
         primaryActionUrl.EndsWith("#billing", StringComparison.OrdinalIgnoreCase))
            ? PaymentController.Localize("payment.workspacePlans")
            : PaymentController.Localize("payment.plans");

    private static string GetPurchaseStatusCode(PaymentPurchaseState? state)
    {
        if (state == null)
        {
            return "purchase_not_found";
        }

        if (state.Result.Status == PaymentCreationStatus.Completed)
        {
            return "confirmed";
        }

        if (state.Result.Status == PaymentCreationStatus.ManualReview)
        {
            return "manual_review";
        }

        if (state.Result.Status == PaymentCreationStatus.Refunded)
        {
            return "refunded";
        }

        if (state.Result.Status == PaymentCreationStatus.PaymentRequired)
        {
            return "pending";
        }

        if (state.IsPaid)
        {
            return "paid_waiting_activation";
        }

        return state.Result.Status switch
        {
            PaymentCreationStatus.Canceled or PaymentCreationStatus.Rejected => "canceled",
            PaymentCreationStatus.InProgress => "pending",
            _ => "returned"
        };
    }
}
