using System.Security.Claims;
using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Moq;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.Services.Interfaces.Payments;
using OpenCRM.WebApp.Controllers.Payments;
using OpenCRM.WebApp.Models.Payments;

namespace OpenCRM.Infrastructure.Tests.Unit;

public sealed class PaymentControllerPurchaseTests : IDisposable
{
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

    public PaymentControllerPurchaseTests()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");
    }

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = this._originalUiCulture;
    }

    [Theory]
    [InlineData(PaymentCreationStatus.InProgress, true, "Оплата получена", false, true)]
    [InlineData(PaymentCreationStatus.PaymentRequired, true, "Оплата ожидает подтверждения", false, true)]
    [InlineData(PaymentCreationStatus.Completed, true, "Оплата подтверждена", true, false)]
    [InlineData(PaymentCreationStatus.ManualReview, true, "Платёж требует проверки", false, false)]
    [InlineData(PaymentCreationStatus.Refunded, true, "Платёж возвращён", false, false)]
    public async Task ExactPurchaseStateNeverFallsBackToAnotherSuccessfulPayment(
        PaymentCreationStatus status, bool isPaid, string title, bool confirmed, bool shouldPoll)
    {
        var purchaseId = Guid.NewGuid();
        var query = new Mock<IPaymentPurchaseQueryService>(MockBehavior.Strict);
        query.Setup(service => service.GetAsync("payer", purchaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentPurchaseState(new(status, PurchaseId: purchaseId), isPaid, "/detailing/org#billing"));
        using var manager = CreateManager();
        var controller = CreateController(manager, query.Object, "?purchaseId=" + purchaseId);
        var model = Assert.IsType<PaymentCompleteViewModel>(Assert.IsType<ViewResult>(await controller.Complete(purchaseId)).Model);
        Assert.Equal(title, model.Title);
        Assert.Equal(confirmed, model.IsConfirmed);
        Assert.Equal(shouldPoll, model.ShouldPollPurchase);
        Assert.Equal(purchaseId, model.PurchaseId);
        if (shouldPoll)
        {
            Assert.Equal($"/api/v1/payments/purchases/{purchaseId:D}", model.PurchaseStatusUrl);
        }
        Assert.Equal("/detailing/org#billing", model.PrimaryActionUrl);
        query.VerifyAll();
    }

    [Fact]
    public async Task MismatchedPurchaseIdIsNotRenderedOrPolled()
    {
        var purchaseId = Guid.NewGuid();
        var otherPurchaseId = Guid.NewGuid();
        var query = new Mock<IPaymentPurchaseQueryService>(MockBehavior.Strict);
        query.Setup(service => service.GetAsync("payer", purchaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentPurchaseState(new(PaymentCreationStatus.Completed, PurchaseId: otherPurchaseId), true, "/other"));
        using var manager = CreateManager();
        var controller = CreateController(manager, query.Object, "?purchaseId=" + purchaseId);

        var model = Assert.IsType<PaymentCompleteViewModel>(Assert.IsType<ViewResult>(await controller.Complete(purchaseId)).Model);

        Assert.Equal("Покупка не найдена", model.Title);
        Assert.False(model.ShouldPollPurchase);
        Assert.Null(model.PurchaseId);
        Assert.Null(model.PurchaseStatusUrl);
        query.VerifyAll();
    }

    [Theory]
    [InlineData("?purchaseId=invalid")]
    [InlineData("?purchaseId=")]
    public async Task InvalidPurchaseIdDoesNotUseLatestLegacyPayment(string queryString)
    {
        using var manager = CreateManager();
        var controller = CreateController(manager, Mock.Of<IPaymentPurchaseQueryService>(MockBehavior.Strict), queryString);
        var model = Assert.IsType<PaymentCompleteViewModel>(Assert.IsType<ViewResult>(await controller.Complete(null)).Model);
        Assert.Equal("Покупка не найдена", model.Title);
        Assert.False(model.IsConfirmed);
    }

    [Fact]
    public void MobileReturnPreservesPurchaseId()
    {
        var purchaseId = Guid.NewGuid();
        using var manager = CreateManager();
        var controller = CreateController(manager, Mock.Of<IPaymentPurchaseQueryService>(MockBehavior.Strict), "");
        var content = controller.MobileComplete(purchaseId).Content;
        Assert.Contains("opencrm://payment/complete?purchaseId=" + purchaseId.ToString("D"), content);
        Assert.Contains("noindex,nofollow", content);
    }

    [Theory]
    [InlineData("en-US", "Payment confirmed", "Returning to the app")]
    [InlineData("zh-Hans", "付款已确认", "正在返回应用")]
    public async Task PaymentPagesUseReaderLanguageAndPreservePurchaseState(
        string cultureName,
        string expectedTitle,
        string expectedMobileTitle)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
        var purchaseId = Guid.NewGuid();
        var query = new Mock<IPaymentPurchaseQueryService>(MockBehavior.Strict);
        query.Setup(service => service.GetAsync("payer", purchaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentPurchaseState(
                new(PaymentCreationStatus.Completed, PurchaseId: purchaseId), true, "/detailing/org#billing"));
        using var manager = PaymentControllerPurchaseTests.CreateManager();
        var controller = PaymentControllerPurchaseTests.CreateController(manager, query.Object, "?purchaseId=" + purchaseId);

        var model = Assert.IsType<PaymentCompleteViewModel>(Assert.IsType<ViewResult>(await controller.Complete(purchaseId)).Model);
        var mobile = WebUtility.HtmlDecode(controller.MobileComplete(purchaseId).Content);

        Assert.Equal(expectedTitle, model.Title);
        Assert.True(model.IsConfirmed);
        Assert.False(model.ShouldPollPurchase);
        Assert.Equal(purchaseId, model.PurchaseId);
        Assert.Equal("/detailing/org#billing", model.PrimaryActionUrl);
        Assert.Contains($"<html lang=\"{cultureName}\">", mobile);
        Assert.Contains(expectedMobileTitle, mobile);
        Assert.Contains("opencrm://payment/complete?purchaseId=" + purchaseId.ToString("D"), mobile);
        query.VerifyAll();
    }

    private static UserManager<ApplicationUser> CreateManager()
    {
        var manager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        manager.Setup(item => item.GetUserId(It.IsAny<ClaimsPrincipal>())).Returns("payer");

        return manager.Object;
    }

    private static PaymentController CreateController(UserManager<ApplicationUser> manager,
        IPaymentPurchaseQueryService query, string queryString)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "payer")], "test"))
        };
        context.Request.QueryString = new QueryString(queryString);
        var urls = new Mock<IUrlHelper>();
        urls.Setup(item => item.Content(It.IsAny<string>()))
            .Returns<string>(path => path.Replace("~", string.Empty, StringComparison.Ordinal));

        return new PaymentController(manager, Mock.Of<IPaymentCompletionService>(MockBehavior.Strict), query)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
            Url = urls.Object
        };
    }
}
