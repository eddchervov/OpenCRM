using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing;
using OpenCRM.Services.Interfaces.FileStorages;

namespace OpenCRM.Modules.Billing.Tests.Unit;

public sealed class AdminNpdReceiptValidationTests
{
    [Theory]
    [InlineData("receipt.txt", "text/plain")]
    [InlineData("receipt.pdf", "image/png")]
    [InlineData("receipt.png", "application/pdf")]
    [InlineData("receipt.jpg", "image/jpg")]
    public async Task UploadRejectsUnsupportedExtensionsAndContentTypes(string fileName, string contentType)
    {
        await using var context = new OpenCRMContext(new DbContextOptionsBuilder<OpenCRMContext>().Options);
        var storage = new Mock<IFileStorageService>(MockBehavior.Strict);
        var service = new AdminNpdReceiptService(context, storage.Object);
        using var content = new MemoryStream("%PDF-1.7"u8.ToArray());

        var result = await service.UploadNpdReceiptFileAsync(1, content, fileName, contentType);

        Assert.False(result.Succeeded);
        Assert.Contains("Разрешены файлы", result.ErrorMessage);
        storage.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("receipt.pdf", "application/pdf", "not pdf")]
    [InlineData("receipt.png", "image/png", "not png")]
    [InlineData("receipt.jpg", "image/jpeg", "not jpg")]
    public async Task UploadRejectsContentThatDoesNotMatchTheExtension(
        string fileName,
        string contentType,
        string contents)
    {
        await using var context = new OpenCRMContext(new DbContextOptionsBuilder<OpenCRMContext>().Options);
        var storage = new Mock<IFileStorageService>(MockBehavior.Strict);
        var service = new AdminNpdReceiptService(context, storage.Object);
        using var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(contents));

        var result = await service.UploadNpdReceiptFileAsync(1, content, fileName, contentType);

        Assert.False(result.Succeeded);
        Assert.Contains("не соответствует", result.ErrorMessage);
        storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UploadRejectsEmptyFilesAndFilesLargerThanFiveMegabytes()
    {
        await using var context = new OpenCRMContext(new DbContextOptionsBuilder<OpenCRMContext>().Options);
        var storage = new Mock<IFileStorageService>(MockBehavior.Strict);
        var service = new AdminNpdReceiptService(context, storage.Object);
        using var empty = new MemoryStream();
        using var oversized = new MemoryStream(new byte[5_242_881]);

        var emptyResult = await service.UploadNpdReceiptFileAsync(1, empty, "receipt.pdf", "application/pdf");
        var oversizedResult = await service.UploadNpdReceiptFileAsync(1, oversized, "receipt.pdf", "application/pdf");

        Assert.False(emptyResult.Succeeded);
        Assert.Contains("от 1 байта до 5 МБ", emptyResult.ErrorMessage);
        Assert.False(oversizedResult.Succeeded);
        Assert.Contains("от 1 байта до 5 МБ", oversizedResult.ErrorMessage);
        storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UploadReportsUnavailableStorageBeforeReadingContent()
    {
        await using var context = new OpenCRMContext(new DbContextOptionsBuilder<OpenCRMContext>().Options);
        var service = new AdminNpdReceiptService(context);
        using var content = new MemoryStream("%PDF-1.7"u8.ToArray());

        var result = await service.UploadNpdReceiptFileAsync(1, content, "receipt.pdf", "application/pdf");

        Assert.False(result.Succeeded);
        Assert.Contains("недоступно", result.ErrorMessage);
        Assert.Equal(0, content.Position);
    }
}
