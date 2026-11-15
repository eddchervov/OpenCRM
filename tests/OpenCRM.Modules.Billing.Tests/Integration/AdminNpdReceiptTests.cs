using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing;
using OpenCRM.Modules.Billing.Contracts.Dtos;
using OpenCRM.Services.Interfaces.FileStorages;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class AdminNpdReceiptTests(BillingDatabaseFixture fixture) : IAsyncLifetime
{
    [Fact]
    public async Task MarkReceiptValidatesInputPaymentAndExpectedState()
    {
        await using var context = fixture.CreateContext();
        var payment = await CreatePaymentAsync(context, "pending", null);
        var service = new AdminNpdReceiptService(context);

        var tooLongReason = await service.MarkNpdReceiptAsync(payment.Id, false, false, new string('x', 501), "admin");
        var missingReason = await service.MarkNpdReceiptAsync(payment.Id, false, false, "   ", "admin");
        var missing = await service.MarkNpdReceiptAsync(int.MaxValue, true, false, null, "admin");
        var unpaid = await service.MarkNpdReceiptAsync(payment.Id, true, false, null, "admin");

        payment.PaidAt = DateTime.UtcNow;
        context.ChangeTracker.Clear();
        context.Update(payment);
        await context.SaveChangesAsync();
        var invalidStatus = await service.MarkNpdReceiptAsync(payment.Id, true, false, null, "admin");
        payment.PaymentStatus = "completed";
        payment.PaidAt = DateTime.UtcNow;
        context.ChangeTracker.Clear();
        context.Update(payment);
        await context.SaveChangesAsync();
        var staleState = await service.MarkNpdReceiptAsync(payment.Id, true, true, null, "admin");

        AssertInvalid(tooLongReason, "500 символов");
        AssertInvalid(missingReason, "Укажите причину");
        Assert.True(missing.NotFound);
        AssertInvalid(unpaid, "подтвержденной оплаты");
        AssertInvalid(invalidStatus, "подтвержденной оплаты");
        AssertInvalid(staleState, "уже изменился");
        Assert.Empty(await context.AdminNpdReceiptAudits.ToListAsync());
    }

    [Fact]
    public async Task MarkAndUnmarkReceiptPersistNormalizedReasonAndAuditRows()
    {
        await using var context = fixture.CreateContext();
        var payment = await CreatePaymentAsync(context, "refunded", DateTime.UtcNow.AddHours(-1));
        var service = new AdminNpdReceiptService(context);

        var issued = await service.MarkNpdReceiptAsync(payment.Id, true, false, "  передан  ", "admin-1");
        var unchanged = await service.MarkNpdReceiptAsync(payment.Id, true, true, null, "admin-2");
        var removed = await service.MarkNpdReceiptAsync(payment.Id, false, true, "  исправление  ", "admin-3");
        var persistedPayment = await context.SubscriptionPayments.AsNoTracking().SingleAsync(item => item.Id == payment.Id);

        Assert.True(issued.Succeeded);
        Assert.Equal("Чек отмечен как выданный и переданный.", issued.Message);
        Assert.True(unchanged.Succeeded);
        Assert.Equal("Статус чека не изменился.", unchanged.Message);
        Assert.True(removed.Succeeded);
        Assert.Equal("Отметка о чеке снята.", removed.Message);
        Assert.Null(persistedPayment.NpdReceiptIssuedAt);
        Assert.Equal("admin-3", persistedPayment.NpdReceiptMarkedByUserId);
        Assert.Equal("исправление", persistedPayment.NpdReceiptReason);

        var audits = await context.AdminNpdReceiptAudits.OrderBy(audit => audit.Id).ToListAsync();
        Assert.Collection(audits,
            audit =>
            {
                Assert.True(audit.Issued);
                Assert.Equal("admin-1", audit.ChangedByUserId);
                Assert.Equal("передан", audit.Reason);
            },
            audit =>
            {
                Assert.False(audit.Issued);
                Assert.Equal("admin-3", audit.ChangedByUserId);
                Assert.Equal("исправление", audit.Reason);
            });
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("failed")]
    public async Task MarkReceiptUsesDefaultReasonAndReportsSuccessfulRemoval(string paymentStatus)
    {
        await using var context = fixture.CreateContext();
        var payment = await CreatePaymentAsync(context, paymentStatus, DateTime.UtcNow);
        var service = new AdminNpdReceiptService(context);

        var issued = await service.MarkNpdReceiptAsync(payment.Id, true, false, null, "admin-1");
        var removed = await service.MarkNpdReceiptAsync(payment.Id, false, true, "исправление", "admin-2");
        var audits = await context.AdminNpdReceiptAudits.OrderBy(audit => audit.Id).ToListAsync();

        Assert.True(issued.Succeeded);
        Assert.Equal("Чек отмечен как выданный и переданный.", issued.Message);
        Assert.True(removed.Succeeded);
        Assert.Equal("Отметка о чеке снята.", removed.Message);
        Assert.Collection(audits,
            audit => Assert.Equal("Чек НПД выдан и передан покупателю.", audit.Reason),
            audit => Assert.Equal("исправление", audit.Reason));
    }

    [Fact]
    public async Task MarkReceiptDefaultsReasonAndRollsBackWhenAuditCannotBeSaved()
    {
        await using var setupContext = fixture.CreateContext();
        var payment = await CreatePaymentAsync(setupContext, "failed", DateTime.UtcNow.AddHours(-1));
        await using var context = fixture.CreateContext(new FailReceiptAuditSaveInterceptor());
        var service = new AdminNpdReceiptService(context);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            service.MarkNpdReceiptAsync(payment.Id, true, false, " ", "admin"));

        await using var verificationContext = fixture.CreateContext();
        var persistedPayment = await verificationContext.SubscriptionPayments.SingleAsync(item => item.Id == payment.Id);
        Assert.Null(persistedPayment.NpdReceiptIssuedAt);
        Assert.Null(persistedPayment.NpdReceiptReason);
        Assert.Empty(await verificationContext.AdminNpdReceiptAudits.ToListAsync());
    }

    [Fact]
    public async Task UpdateReceiptDetailsValidatesUrlLengthAndPaymentEligibility()
    {
        await using var context = fixture.CreateContext();
        var pending = await CreatePaymentAsync(context, "pending", null);
        var service = new AdminNpdReceiptService(context);

        var tooLong = await service.UpdateNpdReceiptDetailsAsync(pending.Id, new string('x', 2049), null);
        var http = await service.UpdateNpdReceiptDetailsAsync(pending.Id, "http://nalog.ru/check", null);
        var malformed = await service.UpdateNpdReceiptDetailsAsync(pending.Id, "not a URL", null);
        var wrongHost = await service.UpdateNpdReceiptDetailsAsync(pending.Id, "https://evil-nalog.ru/check", null);
        var missing = await service.UpdateNpdReceiptDetailsAsync(int.MaxValue, null, null);
        pending.PaidAt = DateTime.UtcNow;
        context.Update(pending);
        await context.SaveChangesAsync();
        var unpaid = await service.UpdateNpdReceiptDetailsAsync(pending.Id, null, null);

        pending.PaidAt = DateTime.UtcNow;
        pending.PaymentStatus = "completed";
        await context.SaveChangesAsync();
        var tooLongComment = await service.UpdateNpdReceiptDetailsAsync(pending.Id, null, new string('x', 1001));

        AssertInvalid(tooLong, "слишком длинные");
        AssertInvalid(http, "HTTPS-ссылку");
        AssertInvalid(malformed, "HTTPS-ссылку");
        AssertInvalid(wrongHost, "HTTPS-ссылку");
        Assert.True(missing.NotFound);
        AssertInvalid(unpaid, "подтвержденной оплате");
        AssertInvalid(tooLongComment, "слишком длинные");
    }

    [Fact]
    public async Task UpdateReceiptDetailsTrimsAndClearsOptionalValues()
    {
        await using var context = fixture.CreateContext();
        var payment = await CreatePaymentAsync(context, "completed", DateTime.UtcNow);
        var service = new AdminNpdReceiptService(context);

        var saved = await service.UpdateNpdReceiptDetailsAsync(payment.Id, "  https://sub.nalog.ru/check/1  ", "  выдано  ");

        Assert.True(saved.Succeeded);
        Assert.Equal("https://sub.nalog.ru/check/1", payment.NpdReceiptUrl);
        Assert.Equal("выдано", payment.NpdReceiptComment);

        var cleared = await service.UpdateNpdReceiptDetailsAsync(payment.Id, "   ", "  ");

        Assert.True(cleared.Succeeded);
        Assert.Null(payment.NpdReceiptUrl);
        Assert.Null(payment.NpdReceiptComment);
    }

    [Theory]
    [InlineData("receipt.pdf", "application/pdf", "completed")]
    [InlineData("receipt.png", "image/png", "refunded")]
    [InlineData("receipt.jpeg", "image/jpeg", "failed")]
    public async Task UploadStoresValidatedFileAndPersistsItsMetadata(
        string fileName,
        string contentType,
        string paymentStatus)
    {
        await using var context = fixture.CreateContext();
        var payment = await CreatePaymentAsync(context, paymentStatus, DateTime.UtcNow);
        var payload = CreateValidContent(fileName);
        string? storedPath = null;
        byte[]? storedContent = null;
        var storage = new Mock<IFileStorageService>(MockBehavior.Strict);
        storage.Setup(item => item.SaveFileAsync(It.IsAny<string>(), It.IsAny<Stream>()))
            .Returns((string path, Stream stream) =>
            {
                storedPath = path;
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                storedContent = copy.ToArray();
                return Task.FromResult("stored/" + Path.GetFileName(path));
            });
        var service = new AdminNpdReceiptService(context, storage.Object);
        using var content = new MemoryStream(payload);

        var result = await service.UploadNpdReceiptFileAsync(payment.Id, content, fileName, contentType);
        await context.Entry(payment).ReloadAsync();

        Assert.True(result.Succeeded);
        Assert.NotNull(storedPath);
        Assert.StartsWith($"admin/npd-receipts/{payment.Id}/", storedPath);
        Assert.EndsWith(Path.GetExtension(fileName), storedPath);
        Assert.Equal(payload, storedContent);
        Assert.Equal("stored/" + Path.GetFileName(storedPath), payment.NpdReceiptFilePath);
        Assert.Equal(Path.GetFileName(fileName), payment.NpdReceiptFileName);
        Assert.Equal(contentType, payment.NpdReceiptFileContentType);
        storage.Verify(item => item.SaveFileAsync(It.IsAny<string>(), It.IsAny<Stream>()), Times.Once);
        storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UploadRejectsShortPdfSignatureAndAcceptsFailedPayment()
    {
        await using var context = fixture.CreateContext();
        var payment = await CreatePaymentAsync(context, "failed", DateTime.UtcNow);
        var storage = new Mock<IFileStorageService>(MockBehavior.Strict);
        storage.Setup(item => item.SaveFileAsync(It.IsAny<string>(), It.IsAny<Stream>()))
            .Returns((string path, Stream _) => Task.FromResult(path));
        var service = new AdminNpdReceiptService(context, storage.Object);
        using var shortPdf = new MemoryStream("%PDF"u8.ToArray());

        var invalid = await service.UploadNpdReceiptFileAsync(payment.Id, shortPdf, "short.pdf", "application/pdf");

        using var validPdf = new MemoryStream("%PDF-1.7"u8.ToArray());
        var uploaded = await service.UploadNpdReceiptFileAsync(payment.Id, validPdf, "receipt.pdf", "application/pdf");

        AssertInvalid(invalid, "не соответствует");
        Assert.True(uploaded.Succeeded);
        Assert.NotNull(payment.NpdReceiptFilePath);
        storage.Verify(item => item.SaveFileAsync(It.IsAny<string>(), It.IsAny<Stream>()), Times.Once);
        storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UploadRequiresStorageAndConfirmedPaymentAndTruncatesLongFileNames()
    {
        await using var context = fixture.CreateContext();
        var unpaid = await CreatePaymentAsync(context, "pending", null);
        var contentBytes = "%PDF-1.7"u8.ToArray();
        var noStorage = new AdminNpdReceiptService(context);
        using var noStorageContent = new MemoryStream(contentBytes);
        var unavailable = await noStorage.UploadNpdReceiptFileAsync(unpaid.Id, noStorageContent, "receipt.pdf", "application/pdf");

        var storage = new Mock<IFileStorageService>(MockBehavior.Strict);
        var service = new AdminNpdReceiptService(context, storage.Object);
        using var pendingContent = new MemoryStream(contentBytes);
        var pending = await service.UploadNpdReceiptFileAsync(unpaid.Id, pendingContent, "receipt.pdf", "application/pdf");
        using var missingContent = new MemoryStream(contentBytes);
        var missing = await service.UploadNpdReceiptFileAsync(int.MaxValue, missingContent, "receipt.pdf", "application/pdf");

        var paid = await CreatePaymentAsync(context, "completed", DateTime.UtcNow);
        storage.Setup(item => item.SaveFileAsync(It.IsAny<string>(), It.IsAny<Stream>()))
            .Returns((string path, Stream _) => Task.FromResult(path));
        unpaid.PaidAt = DateTime.UtcNow;
        context.Update(unpaid);
        await context.SaveChangesAsync();
        using var invalidStatusContent = new MemoryStream(contentBytes);
        var invalidStatus = await service.UploadNpdReceiptFileAsync(unpaid.Id, invalidStatusContent, "receipt.pdf", "application/pdf");
        using var longNameContent = new MemoryStream(contentBytes);
        var longName = new string('a', 205) + ".pdf";
        var uploaded = await service.UploadNpdReceiptFileAsync(paid.Id, longNameContent, longName, "APPLICATION/PDF");

        AssertInvalid(unavailable, "недоступно");
        AssertInvalid(pending, "подтвержденной оплате");
        Assert.True(missing.NotFound);
        AssertInvalid(invalidStatus, "подтвержденной оплате");
        Assert.True(uploaded.Succeeded);
        Assert.Equal(200, paid.NpdReceiptFileName?.Length);
        storage.Verify(item => item.SaveFileAsync(It.IsAny<string>(), It.IsAny<Stream>()), Times.Once);
    }

    [Fact]
    public async Task UploadPropagatesStorageAndDatabaseFailuresWithoutClaimingSuccess()
    {
        await using var setupContext = fixture.CreateContext();
        var storageFailurePayment = await CreatePaymentAsync(setupContext, "completed", DateTime.UtcNow);
        var databaseFailurePayment = await CreatePaymentAsync(setupContext, "completed", DateTime.UtcNow);

        var storageFailure = new Mock<IFileStorageService>(MockBehavior.Strict);
        storageFailure.Setup(item => item.SaveFileAsync(It.IsAny<string>(), It.IsAny<Stream>()))
            .ThrowsAsync(new IOException("storage failure"));
        await using var storageContext = fixture.CreateContext();
        var storageService = new AdminNpdReceiptService(storageContext, storageFailure.Object);
        using var storageContent = new MemoryStream("%PDF-1.7"u8.ToArray());
        await Assert.ThrowsAsync<IOException>(() => storageService.UploadNpdReceiptFileAsync(
            storageFailurePayment.Id, storageContent, "receipt.pdf", "application/pdf"));

        var databaseFailure = new Mock<IFileStorageService>(MockBehavior.Strict);
        databaseFailure.Setup(item => item.SaveFileAsync(It.IsAny<string>(), It.IsAny<Stream>()))
            .Returns((string path, Stream _) => Task.FromResult(path));
        await using var failedContext = fixture.CreateContext(new FailPaymentSaveInterceptor());
        var failedService = new AdminNpdReceiptService(failedContext, databaseFailure.Object);
        using var databaseContent = new MemoryStream("%PDF-1.7"u8.ToArray());
        await Assert.ThrowsAsync<DbUpdateException>(() => failedService.UploadNpdReceiptFileAsync(
            databaseFailurePayment.Id, databaseContent, "receipt.pdf", "application/pdf"));

        await using var verificationContext = fixture.CreateContext();
        var persisted = await verificationContext.SubscriptionPayments.SingleAsync(item => item.Id == databaseFailurePayment.Id);
        Assert.Null(persisted.NpdReceiptFilePath);
        Assert.Null(persisted.NpdReceiptFileName);
        storageFailure.Verify(item => item.SaveFileAsync(It.IsAny<string>(), It.IsAny<Stream>()), Times.Once);
        databaseFailure.Verify(item => item.SaveFileAsync(It.IsAny<string>(), It.IsAny<Stream>()), Times.Once);
    }

    [Fact]
    public async Task GetReceiptFileHandlesMissingStorageAndMissingPathAndUsesMetadataDefaults()
    {
        await using var context = fixture.CreateContext();
        var noFilePayment = await CreatePaymentAsync(context, "completed", DateTime.UtcNow);
        var filePayment = await CreatePaymentAsync(context, "completed", DateTime.UtcNow);
        filePayment.NpdReceiptFilePath = "receipts/file.pdf";
        await context.SaveChangesAsync();
        var noStorage = new AdminNpdReceiptService(context);

        Assert.Null(await noStorage.GetNpdReceiptFileAsync(int.MaxValue));
        Assert.Null(await noStorage.GetNpdReceiptFileAsync(noFilePayment.Id));

        var storedContent = new MemoryStream("file bytes"u8.ToArray());
        var storage = new Mock<IFileStorageService>(MockBehavior.Strict);
        storage.Setup(item => item.GetFileAsync("receipts/file.pdf")).ReturnsAsync(storedContent);
        var service = new AdminNpdReceiptService(context, storage.Object);

        var file = await service.GetNpdReceiptFileAsync(filePayment.Id);

        Assert.NotNull(file);
        Assert.Same(storedContent, file.Content);
        Assert.Equal("receipt", file.FileName);
        Assert.Equal("application/octet-stream", file.ContentType);
        storage.Verify(item => item.GetFileAsync("receipts/file.pdf"), Times.Once);
        storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetReceiptFileReturnsPersistedNameAndMimeType()
    {
        await using var context = fixture.CreateContext();
        var payment = await CreatePaymentAsync(context, "completed", DateTime.UtcNow);
        payment.NpdReceiptFilePath = "receipts/file.png";
        payment.NpdReceiptFileName = "custom.png";
        payment.NpdReceiptFileContentType = "image/png";
        await context.SaveChangesAsync();
        var content = new MemoryStream([1, 2, 3]);
        var storage = new Mock<IFileStorageService>(MockBehavior.Strict);
        storage.Setup(item => item.GetFileAsync(payment.NpdReceiptFilePath)).ReturnsAsync(content);
        var service = new AdminNpdReceiptService(context, storage.Object);

        var file = await service.GetNpdReceiptFileAsync(payment.Id);

        Assert.NotNull(file);
        Assert.Same(content, file.Content);
        Assert.Equal("custom.png", file.FileName);
        Assert.Equal("image/png", file.ContentType);
        storage.Verify(item => item.GetFileAsync("receipts/file.png"), Times.Once);
        storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeleteReceiptFileDetachesMetadataWithoutDeletingStorageObjectAndCanRepeat()
    {
        await using var context = fixture.CreateContext();
        var payment = await CreatePaymentAsync(context, "completed", DateTime.UtcNow);
        payment.NpdReceiptFilePath = "receipts/keep-in-storage.pdf";
        payment.NpdReceiptFileName = "receipt.pdf";
        payment.NpdReceiptFileContentType = "application/pdf";
        await context.SaveChangesAsync();
        var storage = new Mock<IFileStorageService>(MockBehavior.Strict);
        var service = new AdminNpdReceiptService(context, storage.Object);

        var detached = await service.DeleteNpdReceiptFileAsync(payment.Id);
        var repeated = await service.DeleteNpdReceiptFileAsync(payment.Id);
        var missing = await service.DeleteNpdReceiptFileAsync(int.MaxValue);
        await context.Entry(payment).ReloadAsync();

        Assert.True(detached.Succeeded);
        Assert.Equal("Файл чека откреплен.", detached.Message);
        Assert.True(repeated.Succeeded);
        Assert.True(missing.NotFound);
        Assert.Null(payment.NpdReceiptFilePath);
        Assert.Null(payment.NpdReceiptFileName);
        Assert.Null(payment.NpdReceiptFileContentType);
        storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UploadLeavesCallerStreamOpenAndSavesBufferFromStart()
    {
        await using var context = fixture.CreateContext();
        var payment = await CreatePaymentAsync(context, "completed", DateTime.UtcNow);
        var storage = new Mock<IFileStorageService>(MockBehavior.Strict);
        byte[]? savedBytes = null;
        storage.Setup(item => item.SaveFileAsync(It.IsAny<string>(), It.IsAny<Stream>()))
            .Returns(async (string path, Stream stream) =>
            {
                Assert.Equal(0, stream.Position);
                using var copy = new MemoryStream();
                await stream.CopyToAsync(copy);
                savedBytes = copy.ToArray();
                return path;
            });
        var service = new AdminNpdReceiptService(context, storage.Object);
        var bytes = "%PDF-1.7"u8.ToArray();
        using var content = new MemoryStream(bytes);

        var result = await service.UploadNpdReceiptFileAsync(payment.Id, content, "receipt.pdf", "application/pdf");

        Assert.True(result.Succeeded);
        Assert.True(content.CanRead);
        content.Position = 0;
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy);
        Assert.Equal(bytes, copy.ToArray());
        Assert.Equal(bytes, savedBytes);
        storage.Verify(item => item.SaveFileAsync(It.IsAny<string>(), It.IsAny<Stream>()), Times.Once);
        storage.VerifyNoOtherCalls();
    }

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<SubscriptionPayment> CreatePaymentAsync(
        OpenCRMContext context,
        string status,
        DateTime? paidAt)
    {
        var user = new ApplicationUser { Email = $"receipt-{Guid.NewGuid():N}@example.test" };
        var plan = new SubscriptionPlan
        {
            Name = "Чек",
            Description = "Тестовый план",
            Price = 500,
            DurationDays = 30,
            VehicleLimit = 1,
            Order = 1,
            Group = "Тест",
            PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal
        };
        var createdAt = paidAt ?? DateTime.UtcNow;
        var payment = new SubscriptionPayment
        {
            User = user,
            SubscriptionPlan = plan,
            Amount = 500,
            RefundedAmount = 0,
            CreatedAt = createdAt,
            StatusUpdatedAt = createdAt,
            PaidAt = paidAt,
            PaymentMethod = "yookassa",
            PaymentStatus = status
        };
        context.SubscriptionPayments.Add(payment);
        await context.SaveChangesAsync();

        return payment;
    }

    private static byte[] CreateValidContent(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => "%PDF-1.7\nbody"u8.ToArray(),
        ".png" => [137, 80, 78, 71, 13, 10, 26, 10, 1],
        ".jpg" or ".jpeg" => [255, 216, 255, 1],
        _ => throw new ArgumentOutOfRangeException(nameof(fileName))
    };

    private static void AssertInvalid(AdminBillingPaymentOperationDto result, string messagePart)
    {
        Assert.False(result.Succeeded);
        Assert.Contains(messagePart, result.ErrorMessage);
    }

    private sealed class FailReceiptAuditSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<AdminNpdReceiptAudit>()
                .Any(entry => entry.State == EntityState.Added) == true)
            {
                throw new DbUpdateException("Тестовая ошибка сохранения аудита.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class FailPaymentSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<SubscriptionPayment>()
                .Any(entry => entry.State == EntityState.Modified) == true)
            {
                throw new DbUpdateException("Тестовая ошибка сохранения платежа.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
