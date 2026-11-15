using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.FileStorages;

namespace OpenCRM.Infrastructure.Tests.Unit;

public sealed class NpdReceiptFileValidationTests
{
    [Fact]
    public async Task PdfAttachmentMustContainPdfData()
    {
        await using var context = new OpenCRMContext(new DbContextOptionsBuilder<OpenCRMContext>().Options);
        var storage = new Mock<IFileStorageService>();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton(storage.Object);
        OpenCRM.Modules.Billing.BillingServiceCollectionExtensions.AddOpenCRMBilling(services);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminNpdReceiptService>();
        using var content = new MemoryStream("not a PDF"u8.ToArray());

        var result = await service.UploadNpdReceiptFileAsync(1, content, "receipt.pdf", "application/pdf");

        Assert.False(result.Succeeded);
        Assert.Contains("не соответствует", result.ErrorMessage);
        storage.Verify(item => item.SaveFileAsync(It.IsAny<string>(), It.IsAny<Stream>()), Times.Never);
    }
}
