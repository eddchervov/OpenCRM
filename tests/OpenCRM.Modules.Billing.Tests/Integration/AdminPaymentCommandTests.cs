using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Billing.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Payments;
using OpenCRM.Services.Interfaces.Payments.Dto;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class AdminPaymentCommandTests(BillingDatabaseFixture fixture) : IAsyncLifetime
{
    [Fact]
    public async Task SyncReturnsMissingAndProviderFailuresWithoutChangingPayment()
    {
        await using var context = fixture.CreateContext();
        var paymentWithoutExternalId = await CreatePaymentAsync(context, "pending", null);
        var paymentWithExternalId = await CreatePaymentAsync(context, "pending", "provider-failure");
        var provider = new Mock<IYooKassaService>(MockBehavior.Strict);
        provider.Setup(service => service.GetPaymentStatusAsync("provider-failure"))
            .ReturnsAsync(PaymentResponse.CreateError("Провайдер недоступен."));
        var confirmation = new Mock<IPaymentConfirmationService>(MockBehavior.Strict);
        var service = CreateService(context, provider, confirmation);

        var missing = await service.SyncPaymentWithYooKassaAsync(int.MaxValue, "admin");
        var noExternalId = await service.SyncPaymentWithYooKassaAsync(paymentWithoutExternalId.Id, "admin");
        var providerFailure = await service.SyncPaymentWithYooKassaAsync(paymentWithExternalId.Id, "admin");

        Assert.True(missing.NotFound);
        Assert.Equal("Платеж не найден.", missing.ErrorMessage);
        Assert.False(noExternalId.Succeeded);
        Assert.Equal("У платежа нет external id YooKassa.", noExternalId.ErrorMessage);
        Assert.Equal("Провайдер недоступен.", providerFailure.ErrorMessage);
        Assert.Equal("pending", (await context.SubscriptionPayments.FindAsync(paymentWithExternalId.Id))!.PaymentStatus);
        Assert.Empty(context.AdminPaymentStatusAudits);
        provider.Verify(service => service.GetPaymentStatusAsync("provider-failure"), Times.Once);
        provider.Verify(service => service.GetPaymentStatusAsync(It.IsAny<string>()), Times.Once);
        provider.VerifyNoOtherCalls();
        confirmation.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("payment-id")]
    [InlineData("currency")]
    [InlineData("amount")]
    [InlineData("negative-refund")]
    [InlineData("excess-refund")]
    [InlineData("paid-canceled")]
    [InlineData("refund-currency")]
    public async Task SyncRejectsRemotePaymentDataThatDoesNotMatchLocalPayment(string mismatch)
    {
        await using var context = fixture.CreateContext();
        var payment = await CreatePaymentAsync(context, "pending", "external-payment");
        var provider = new Mock<IYooKassaService>(MockBehavior.Strict);
        provider.Setup(service => service.GetPaymentStatusAsync(payment.ExternalPaymentId!))
            .ReturnsAsync(CreateRemoteResponse(mismatch));
        var confirmation = new Mock<IPaymentConfirmationService>(MockBehavior.Strict);
        var service = CreateService(context, provider, confirmation);

        var result = await service.SyncPaymentWithYooKassaAsync(payment.Id, "admin");

        Assert.False(result.Succeeded);
        Assert.Equal("Данные платежа в YooKassa не совпадают с локальными.", result.ErrorMessage);
        var persisted = await context.SubscriptionPayments.FindAsync(payment.Id);
        Assert.Equal("pending", persisted!.PaymentStatus);
        Assert.Equal(0, persisted.RefundedAmount);
        Assert.Null(persisted.LastReconciledAt);
        Assert.Empty(context.AdminPaymentStatusAudits);
        provider.Verify(service => service.GetPaymentStatusAsync(payment.ExternalPaymentId!), Times.Once);
        provider.Verify(service => service.GetPaymentStatusAsync(It.IsAny<string>()), Times.Once);
        provider.VerifyNoOtherCalls();
        confirmation.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("waiting_for_capture")]
    [InlineData("paid_waiting_confirmation")]
    public async Task SyncConfirmsSucceededPaymentsAndReloadsTheActivatedPayment(string localStatus)
    {
        await using var context = fixture.CreateContext();
        var payment = await CreatePaymentAsync(context, localStatus, "activate-payment");
        var provider = new Mock<IYooKassaService>(MockBehavior.Strict);
        provider.Setup(service => service.GetPaymentStatusAsync("activate-payment"))
            .ReturnsAsync(CreateRemoteResponse("valid", "activate-payment", status: "succeeded", paid: true,
                createdAt: FixedRemoteDate));
        var confirmation = new Mock<IPaymentConfirmationService>(MockBehavior.Strict);
        confirmation.Setup(service => service.ConfirmAsync("activate-payment"))
            .Returns(async () =>
            {
                await using var confirmationContext = fixture.CreateContext();
                var confirmedPayment = await confirmationContext.SubscriptionPayments.FindAsync(payment.Id);
                confirmedPayment!.PaymentStatus = "completed";
                confirmedPayment.PaidAt = FixedPaidDate;
                await confirmationContext.SaveChangesAsync();

                return true;
            });
        var service = CreateService(context, provider, confirmation);

        var result = await service.SyncPaymentWithYooKassaAsync(payment.Id, "sync-admin");

        Assert.True(result.Succeeded);
        Assert.Equal("completed", result.Status);
        Assert.Equal("Оплачено", result.StatusName);
        Assert.Equal("success", result.StatusGroup);
        Assert.Equal("succeeded", result.RemoteStatus);
        Assert.True(result.Paid);
        Assert.Equal(500, result.Amount);
        var persisted = await ReloadPaymentAsync(payment.Id);
        Assert.Equal("completed", persisted.PaymentStatus);
        Assert.Equal(FixedRemoteDate, persisted.CreatedAt);
        Assert.Equal(FixedPaidDate, persisted.PaidAt);
        Assert.NotNull(persisted.LastReconciledAt);
        Assert.Empty(await ReadAuditsAsync(payment.Id));
        confirmation.Verify(service => service.ConfirmAsync("activate-payment"), Times.Once);
        confirmation.VerifyNoOtherCalls();
        provider.Verify(service => service.GetPaymentStatusAsync(It.IsAny<string>()), Times.Once);
        provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SyncRequiresConfirmationServiceBeforeChangingPaidPayment()
    {
        await using var context = fixture.CreateContext();
        var payment = await CreatePaymentAsync(context, "pending", "activation-unavailable");
        var provider = new Mock<IYooKassaService>(MockBehavior.Strict);
        provider.Setup(service => service.GetPaymentStatusAsync("activation-unavailable"))
            .ReturnsAsync(CreateRemoteResponse("valid", "activation-unavailable", status: "succeeded", paid: true));
        var service = new AdminPaymentCommandService(context, provider.Object);

        var result = await service.SyncPaymentWithYooKassaAsync(payment.Id, "admin");

        Assert.False(result.Succeeded);
        Assert.Equal("Сервис активации подписки недоступен.", result.ErrorMessage);
        Assert.Equal("pending", (await ReloadPaymentAsync(payment.Id)).PaymentStatus);
        Assert.Empty(await ReadAuditsAsync(payment.Id));
        provider.Verify(service => service.GetPaymentStatusAsync(It.IsAny<string>()), Times.Once);
        provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SyncRejectsSucceededPaymentWhenConfirmationDidNotActivateSubscription()
    {
        await using var context = fixture.CreateContext();
        var payment = await CreatePaymentAsync(context, "pending", "confirmation-no-activation");
        var provider = new Mock<IYooKassaService>(MockBehavior.Strict);
        provider.Setup(service => service.GetPaymentStatusAsync("confirmation-no-activation"))
            .ReturnsAsync(CreateRemoteResponse("valid", "confirmation-no-activation", status: "succeeded", paid: true));
        var confirmation = new Mock<IPaymentConfirmationService>(MockBehavior.Strict);
        confirmation.Setup(service => service.ConfirmAsync("confirmation-no-activation")).ReturnsAsync(false);
        var service = CreateService(context, provider, confirmation);

        var result = await service.SyncPaymentWithYooKassaAsync(payment.Id, "admin");

        Assert.False(result.Succeeded);
        Assert.Equal("Оплата подтверждена, но подписка еще не активирована. Проверьте журнал и повторите сверку позже.",
            result.ErrorMessage);
        Assert.Equal("pending", (await ReloadPaymentAsync(payment.Id)).PaymentStatus);
        Assert.Empty(await ReadAuditsAsync(payment.Id));
        confirmation.Verify(service => service.ConfirmAsync("confirmation-no-activation"), Times.Once);
        confirmation.VerifyNoOtherCalls();
        provider.Verify(service => service.GetPaymentStatusAsync(It.IsAny<string>()), Times.Once);
        provider.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("failed")]
    public async Task SyncDoesNotConfirmAlreadyResolvedOrFailedPayment(string localStatus)
    {
        await using var context = fixture.CreateContext();
        var payment = await CreatePaymentAsync(context, localStatus, "already-resolved");
        var provider = new Mock<IYooKassaService>(MockBehavior.Strict);
        provider.Setup(service => service.GetPaymentStatusAsync("already-resolved"))
            .ReturnsAsync(CreateRemoteResponse("valid", "already-resolved", status: "succeeded", paid: true,
                refundedAmount: 100, refundedCurrency: "RUB"));
        var confirmation = new Mock<IPaymentConfirmationService>(MockBehavior.Strict);
        var service = CreateService(context, provider, confirmation);

        var result = await service.SyncPaymentWithYooKassaAsync(payment.Id, "admin");

        Assert.True(result.Succeeded);
        Assert.Equal(localStatus, result.Status);
        Assert.Equal("Статус не изменился, данные оплаты сверены с YooKassa.", result.Message);
        Assert.Equal(100, (await ReloadPaymentAsync(payment.Id)).RefundedAmount);
        Assert.Empty(await ReadAuditsAsync(payment.Id));
        confirmation.VerifyNoOtherCalls();
        provider.Verify(service => service.GetPaymentStatusAsync(It.IsAny<string>()), Times.Once);
        provider.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("refunded")]
    public async Task SyncRejectsRemoteStateContradictingCompletedPayment(string localStatus)
    {
        await using var context = fixture.CreateContext();
        var payment = await CreatePaymentAsync(context, localStatus, "contradictory-payment", paidAt: FixedPaidDate);
        var provider = new Mock<IYooKassaService>(MockBehavior.Strict);
        provider.Setup(service => service.GetPaymentStatusAsync("contradictory-payment"))
            .ReturnsAsync(CreateRemoteResponse("valid", "contradictory-payment", status: "pending", paid: false));
        var confirmation = new Mock<IPaymentConfirmationService>(MockBehavior.Strict);
        var service = CreateService(context, provider, confirmation);

        var result = await service.SyncPaymentWithYooKassaAsync(payment.Id, "admin");

        Assert.False(result.Succeeded);
        Assert.Equal("YooKassa вернула состояние, противоречащее уже подтвержденной оплате. Проверьте платеж вручную.",
            result.ErrorMessage);
        Assert.Equal(localStatus, (await ReloadPaymentAsync(payment.Id)).PaymentStatus);
        Assert.Equal(FixedPaidDate, (await ReloadPaymentAsync(payment.Id)).PaidAt);
        Assert.Empty(await ReadAuditsAsync(payment.Id));
        confirmation.VerifyNoOtherCalls();
        provider.Verify(service => service.GetPaymentStatusAsync(It.IsAny<string>()), Times.Once);
        provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SyncUpdatesDatesAndRefundAmountWithoutDuplicateStatusAudit()
    {
        await using var context = fixture.CreateContext();
        var payment = await CreatePaymentAsync(context, "completed", "refresh-completed", paidAt: null);
        var provider = new Mock<IYooKassaService>(MockBehavior.Strict);
        provider.Setup(service => service.GetPaymentStatusAsync("refresh-completed"))
            .ReturnsAsync(CreateRemoteResponse("valid", "refresh-completed", status: "succeeded", paid: true,
                refundedAmount: 25, refundedCurrency: "RUB", createdAt: FixedRemoteDate));
        var confirmation = new Mock<IPaymentConfirmationService>(MockBehavior.Strict);
        var service = CreateService(context, provider, confirmation);

        var result = await service.SyncPaymentWithYooKassaAsync(payment.Id, "admin");

        Assert.True(result.Succeeded);
        Assert.Equal("Статус не изменился, данные оплаты сверены с YooKassa.", result.Message);
        var persisted = await ReloadPaymentAsync(payment.Id);
        Assert.Equal(FixedRemoteDate, persisted.CreatedAt);
        Assert.NotNull(persisted.PaidAt);
        Assert.Equal(25, persisted.RefundedAmount);
        Assert.NotNull(persisted.LastReconciledAt);
        Assert.Empty(await ReadAuditsAsync(payment.Id));
        confirmation.VerifyNoOtherCalls();
        provider.Verify(service => service.GetPaymentStatusAsync(It.IsAny<string>()), Times.Once);
        provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SyncKeepsDefaultRemoteDateAndUsesUnchangedStatusMessage()
    {
        await using var context = fixture.CreateContext();
        var payment = await CreatePaymentAsync(context, "pending", "unchanged-pending", createdAt: FixedLocalDate);
        var provider = new Mock<IYooKassaService>(MockBehavior.Strict);
        provider.Setup(service => service.GetPaymentStatusAsync("unchanged-pending"))
            .ReturnsAsync(CreateRemoteResponse("valid", "unchanged-pending", status: "pending", paid: false,
                createdAt: DateTime.MinValue));
        var confirmation = new Mock<IPaymentConfirmationService>(MockBehavior.Strict);
        var service = CreateService(context, provider, confirmation);

        var result = await service.SyncPaymentWithYooKassaAsync(payment.Id, "admin");

        Assert.True(result.Succeeded);
        Assert.Equal("Статус не изменился: Ожидание.", result.Message);
        Assert.Equal(FixedLocalDate, (await ReloadPaymentAsync(payment.Id)).CreatedAt);
        Assert.Empty(await ReadAuditsAsync(payment.Id));
        confirmation.VerifyNoOtherCalls();
        provider.Verify(service => service.GetPaymentStatusAsync(It.IsAny<string>()), Times.Once);
        provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SyncClearsPaidDateWhenRemoteCanceledWithoutChangingStatus()
    {
        await using var context = fixture.CreateContext();
        var payment = await CreatePaymentAsync(context, "canceled", "same-canceled", paidAt: FixedPaidDate);
        var provider = new Mock<IYooKassaService>(MockBehavior.Strict);
        provider.Setup(service => service.GetPaymentStatusAsync("same-canceled"))
            .ReturnsAsync(CreateRemoteResponse("valid", "same-canceled", status: "canceled", paid: false));
        var confirmation = new Mock<IPaymentConfirmationService>(MockBehavior.Strict);
        var service = CreateService(context, provider, confirmation);

        var result = await service.SyncPaymentWithYooKassaAsync(payment.Id, "admin");

        Assert.True(result.Succeeded);
        Assert.Equal("YooKassa подтвердила отмену. Ошибочная дата оплаты удалена; проверьте доступ по подписке.",
            result.Message);
        Assert.Null((await ReloadPaymentAsync(payment.Id)).PaidAt);
        Assert.Empty(await ReadAuditsAsync(payment.Id));
        confirmation.VerifyNoOtherCalls();
        provider.Verify(service => service.GetPaymentStatusAsync(It.IsAny<string>()), Times.Once);
        provider.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("completed", true)]
    [InlineData("refunded", true)]
    [InlineData("failed", false)]
    public async Task SyncMapsCanceledRemoteStateAndAuditsChangedStatus(
        string previousStatus,
        bool wasPreviouslyPaid)
    {
        await using var context = fixture.CreateContext();
        var payment = await CreatePaymentAsync(context, previousStatus, "remote-canceled", paidAt: FixedPaidDate);
        var provider = new Mock<IYooKassaService>(MockBehavior.Strict);
        provider.Setup(service => service.GetPaymentStatusAsync("remote-canceled"))
            .ReturnsAsync(CreateRemoteResponse("valid", "remote-canceled", status: "canceled", paid: false));
        var confirmation = new Mock<IPaymentConfirmationService>(MockBehavior.Strict);
        var service = CreateService(context, provider, confirmation);

        var result = await service.SyncPaymentWithYooKassaAsync(payment.Id, "sync-admin");

        Assert.True(result.Succeeded);
        Assert.Equal("canceled", result.Status);
        Assert.Equal("Отменено", result.StatusName);
        Assert.Equal("canceled", result.StatusGroup);
        Assert.Equal(wasPreviouslyPaid
                ? "YooKassa подтвердила отмену. Покупка удалена из списка; проверьте доступ по подписке."
                : "Статус обновлен: Отменено.",
            result.Message);
        Assert.Null((await ReloadPaymentAsync(payment.Id)).PaidAt);
        var audit = Assert.Single(await ReadAuditsAsync(payment.Id));
        Assert.Equal(previousStatus, audit.PreviousStatus);
        Assert.Equal("canceled", audit.NewStatus);
        Assert.Equal("yookassa_sync", audit.Source);
        Assert.Equal("sync-admin", audit.ChangedByUserId);
        confirmation.VerifyNoOtherCalls();
        provider.Verify(service => service.GetPaymentStatusAsync(It.IsAny<string>()), Times.Once);
        provider.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("waiting_for_capture", "waiting_for_capture", "Ждет подтверждения", "pending")]
    [InlineData("", "pending", "Ожидание", "pending")]
    [InlineData(null, "pending", "Ожидание", "pending")]
    [InlineData("succeeded", "paid_waiting_confirmation", "Оплачен, ждет подтверждения", "pending")]
    [InlineData("authorized", "authorized", "authorized", "other")]
    public async Task SyncMapsNonPaidRemoteStatusesAndAuditsChangedStatus(
        string? remoteStatus,
        string expectedStatus,
        string expectedName,
        string expectedGroup)
    {
        await using var context = fixture.CreateContext();
        var payment = await CreatePaymentAsync(context, "failed", "remote-status");
        var provider = new Mock<IYooKassaService>(MockBehavior.Strict);
        provider.Setup(service => service.GetPaymentStatusAsync("remote-status"))
            .ReturnsAsync(CreateRemoteResponse("valid", "remote-status", status: remoteStatus, paid: false));
        var confirmation = new Mock<IPaymentConfirmationService>(MockBehavior.Strict);
        var service = CreateService(context, provider, confirmation);

        var result = await service.SyncPaymentWithYooKassaAsync(payment.Id, "sync-admin");

        Assert.True(result.Succeeded);
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedName, result.StatusName);
        Assert.Equal(expectedGroup, result.StatusGroup);
        Assert.Equal($"Статус обновлен: {expectedName}.", result.Message);
        var audit = Assert.Single(await ReadAuditsAsync(payment.Id));
        Assert.Equal("failed", audit.PreviousStatus);
        Assert.Equal(expectedStatus, audit.NewStatus);
        Assert.Null(audit.Reason);
        confirmation.VerifyNoOtherCalls();
        provider.Verify(service => service.GetPaymentStatusAsync(It.IsAny<string>()), Times.Once);
        provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SyncMarksFullyRefundedSucceededPaymentAsRefunded()
    {
        await using var context = fixture.CreateContext();
        var payment = await CreatePaymentAsync(context, "completed", "fully-refunded");
        var provider = new Mock<IYooKassaService>(MockBehavior.Strict);
        provider.Setup(service => service.GetPaymentStatusAsync("fully-refunded"))
            .ReturnsAsync(CreateRemoteResponse("valid", "fully-refunded", status: "succeeded", paid: true,
                refundedAmount: 500, refundedCurrency: "RUB"));
        var confirmation = new Mock<IPaymentConfirmationService>(MockBehavior.Strict);
        var service = CreateService(context, provider, confirmation);

        var result = await service.SyncPaymentWithYooKassaAsync(payment.Id, "sync-admin");

        Assert.True(result.Succeeded);
        Assert.Equal("refunded", result.Status);
        Assert.Equal("Возврат", result.StatusName);
        Assert.Equal("refunded", result.StatusGroup);
        Assert.Equal("Статус обновлен: Возврат.", result.Message);
        var audit = Assert.Single(await ReadAuditsAsync(payment.Id));
        Assert.Equal("completed", audit.PreviousStatus);
        Assert.Equal("refunded", audit.NewStatus);
        confirmation.VerifyNoOtherCalls();
        provider.Verify(service => service.GetPaymentStatusAsync(It.IsAny<string>()), Times.Once);
        provider.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown")]
    public async Task ManualStatusRejectsUnsupportedStatuses(string? status)
    {
        await using var context = fixture.CreateContext();
        var payment = await CreatePaymentAsync(context, "pending", "manual-invalid-status");
        var service = CreateService(context, new Mock<IYooKassaService>(MockBehavior.Strict));

        var result = await service.MarkPaymentStatusAsync(payment.Id, status, "pending", "Причина", "admin");

        Assert.False(result.Succeeded);
        Assert.Equal("Недопустимый статус платежа.", result.ErrorMessage);
        Assert.Equal("pending", (await ReloadPaymentAsync(payment.Id)).PaymentStatus);
        Assert.Empty(await ReadAuditsAsync(payment.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ManualStatusRequiresAReason(string? reason)
    {
        await using var context = fixture.CreateContext();
        var payment = await CreatePaymentAsync(context, "pending", "manual-no-reason");
        var service = CreateService(context, new Mock<IYooKassaService>(MockBehavior.Strict));

        var result = await service.MarkPaymentStatusAsync(payment.Id, "failed", "pending", reason, "admin");

        Assert.False(result.Succeeded);
        Assert.Equal("Укажите причину ручной корректировки.", result.ErrorMessage);
        Assert.Equal("pending", (await ReloadPaymentAsync(payment.Id)).PaymentStatus);
        Assert.Empty(await ReadAuditsAsync(payment.Id));
    }

    [Fact]
    public async Task ManualStatusRejectsReasonLongerThanFiveHundredCharacters()
    {
        await using var context = fixture.CreateContext();
        var payment = await CreatePaymentAsync(context, "pending", "manual-long-reason");
        var service = CreateService(context, new Mock<IYooKassaService>(MockBehavior.Strict));

        var result = await service.MarkPaymentStatusAsync(payment.Id, "failed", "pending", new string('a', 501), "admin");

        Assert.False(result.Succeeded);
        Assert.Equal("Причина ручной корректировки не должна превышать 500 символов.", result.ErrorMessage);
        Assert.Empty(await ReadAuditsAsync(payment.Id));
    }

    [Theory]
    [InlineData("completed")]
    [InlineData(null)]
    public async Task ManualStatusReturnsMissingAndRejectsStaleExpectedStatus(string? expectedStatus)
    {
        await using var context = fixture.CreateContext();
        var payment = await CreatePaymentAsync(context, "pending", "manual-stale");
        var service = CreateService(context, new Mock<IYooKassaService>(MockBehavior.Strict));

        var missing = await service.MarkPaymentStatusAsync(int.MaxValue, "failed", "pending", "Причина", "admin");
        var stale = await service.MarkPaymentStatusAsync(payment.Id, "failed", expectedStatus, "Причина", "admin");

        Assert.True(missing.NotFound);
        Assert.Equal("Платеж не найден.", missing.ErrorMessage);
        Assert.False(stale.Succeeded);
        Assert.Equal("Статус платежа уже изменился. Обновите список и повторите операцию.", stale.ErrorMessage);
        Assert.Equal("pending", (await ReloadPaymentAsync(payment.Id)).PaymentStatus);
        Assert.Empty(await ReadAuditsAsync(payment.Id));
    }

    [Theory]
    [InlineData("failed", "Ошибка", "problem")]
    [InlineData("refunded", "Возврат", "refunded")]
    [InlineData("canceled", "Отменено", "canceled")]
    [InlineData("pending", "Ожидание", "pending")]
    [InlineData("completed", "Оплачено", "success")]
    public async Task ManualStatusChangesPaymentAndWritesAudit(
        string status,
        string expectedName,
        string expectedGroup)
    {
        await using var context = fixture.CreateContext();
        var currentStatus = status == "pending" ? "failed" : "pending";
        var payment = await CreatePaymentAsync(context, currentStatus, "manual-update");
        var service = CreateService(context, new Mock<IYooKassaService>(MockBehavior.Strict));

        var result = await service.MarkPaymentStatusAsync(
            payment.Id,
            $" {status.ToUpperInvariant()} ",
            $" {currentStatus.ToUpperInvariant()} ",
            "  Проверка администратором  ",
            "manual-admin");

        Assert.True(result.Succeeded);
        Assert.Equal(status, result.Status);
        Assert.Equal(expectedName, result.StatusName);
        Assert.Equal(expectedGroup, result.StatusGroup);
        Assert.Equal($"Платеж помечен: {expectedName}.", result.Message);
        var persisted = await ReloadPaymentAsync(payment.Id);
        Assert.Equal(status, persisted.PaymentStatus);
        Assert.NotNull(persisted.StatusUpdatedAt);
        var audit = Assert.Single(await ReadAuditsAsync(payment.Id));
        Assert.Equal(currentStatus, audit.PreviousStatus);
        Assert.Equal(status, audit.NewStatus);
        Assert.Equal("manual", audit.Source);
        Assert.Equal("Проверка администратором", audit.Reason);
        Assert.Equal("manual-admin", audit.ChangedByUserId);
    }

    [Fact]
    public async Task ManualStatusIsIdempotentAndDoesNotCreateAnotherAudit()
    {
        await using var context = fixture.CreateContext();
        var payment = await CreatePaymentAsync(context, " PENDING ", "manual-idempotent");
        var service = CreateService(context, new Mock<IYooKassaService>(MockBehavior.Strict));

        var result = await service.MarkPaymentStatusAsync(payment.Id, "pending", " pending ", "Повтор", "admin");

        Assert.True(result.Succeeded);
        Assert.Equal("У платежа уже установлен выбранный статус.", result.Message);
        Assert.Equal(" PENDING ", result.Status);
        Assert.Equal("Ожидание", result.StatusName);
        Assert.Equal("pending", result.StatusGroup);
        Assert.Null((await ReloadPaymentAsync(payment.Id)).StatusUpdatedAt);
        Assert.Empty(await ReadAuditsAsync(payment.Id));
    }

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly DateTime FixedLocalDate = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime FixedRemoteDate = new(2024, 2, 2, 12, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime FixedPaidDate = new(2024, 2, 3, 12, 30, 0, DateTimeKind.Utc);

    private static AdminPaymentCommandService CreateService(
        OpenCRMContext context,
        Mock<IYooKassaService> provider,
        Mock<IPaymentConfirmationService>? confirmation = null)
    {
        return new AdminPaymentCommandService(context, provider.Object, confirmation?.Object);
    }

    private async Task<SubscriptionPayment> CreatePaymentAsync(
        OpenCRMContext context,
        string status,
        string? externalPaymentId,
        DateTime? createdAt = null,
        DateTime? paidAt = null)
    {
        var user = new ApplicationUser { Email = $"admin-payment-{Guid.NewGuid():N}@example.test" };
        context.Users.Add(user);
        var plan = await fixture.CreatePlanAsync(context, $"План {Guid.NewGuid():N}");
        var payment = new SubscriptionPayment
        {
            User = user,
            SubscriptionPlan = plan,
            Amount = 500,
            RefundedAmount = 0,
            CreatedAt = createdAt ?? FixedLocalDate,
            PaymentMethod = "yookassa",
            PaymentStatus = status,
            ExternalPaymentId = externalPaymentId,
            PaidAt = paidAt
        };
        context.SubscriptionPayments.Add(payment);
        await context.SaveChangesAsync();

        return payment;
    }

    private static PaymentResponse CreateRemoteResponse(
        string mismatch,
        string paymentId = "external-payment",
        string? status = "pending",
        bool paid = false,
        decimal refundedAmount = 0,
        string? refundedCurrency = null,
        DateTime? createdAt = null)
    {
        var response = new PaymentResponse
        {
            IsSuccess = true,
            PaymentId = paymentId,
            Status = status,
            Paid = paid,
            Currency = "RUB",
            Amount = 500,
            RefundedAmount = refundedAmount,
            RefundedCurrency = refundedCurrency,
            CreatedAt = createdAt
        };

        switch (mismatch)
        {
            case "payment-id":
                response.PaymentId = "different-payment";
                break;
            case "currency":
                response.Currency = "USD";
                break;
            case "amount":
                response.Amount = 501;
                break;
            case "negative-refund":
                response.RefundedAmount = -1;
                break;
            case "excess-refund":
                response.RefundedAmount = 501;
                break;
            case "paid-canceled":
                response.Status = "canceled";
                response.Paid = true;
                break;
            case "refund-currency":
                response.RefundedAmount = 1;
                response.RefundedCurrency = "USD";
                break;
        }

        return response;
    }

    private async Task<SubscriptionPayment> ReloadPaymentAsync(int paymentId)
    {
        await using var context = fixture.CreateContext();

        return (await context.SubscriptionPayments.FindAsync(paymentId))!;
    }

    private async Task<List<AdminPaymentStatusAudit>> ReadAuditsAsync(int paymentId)
    {
        await using var context = fixture.CreateContext();

        return await context.AdminPaymentStatusAudits.Where(audit => audit.PaymentId == paymentId).ToListAsync();
    }
}
