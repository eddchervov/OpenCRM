using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Notifications;
using OpenCRM.Modules.Notifications.Emails;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Testing;
using Notifications.Contracts;

namespace OpenCRM.Modules.Notifications.Tests.Integration;

[Collection(nameof(NotificationDatabaseCollection))]
public sealed class NotificationAsyncTransactionTests(NotificationFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("delivery")]
    [InlineData("status")]
    [InlineData("campaign")]
    public async Task HandlerFinishesOnlyAfterAsynchronousTransactionDisposal(string handler)
    {
        await using var original = fixture.CreateContext();
        var options = (DbContextOptions<OpenCRMContext>)original.GetService<IDbContextOptions>();
        await using var context = new OpenCRMContext(new DbContextOptionsBuilder<OpenCRMContext>(options)
            .ReplaceService<IRelationalTransactionFactory, AsyncDisposalTransactionFactory>().Options);
        var transactions = (AsyncDisposalTransactionFactory)context.GetService<IRelationalTransactionFactory>();
        var rabbitOptions = Options.Create(fixture.Options);
        switch (handler)
        {
            case "delivery":
                var delivery = new NotificationDeliveryReported(Guid.NewGuid(), fixture.Options.ApplicationId,
                    Guid.NewGuid().ToString(), "delivered", DateTimeOffset.UtcNow, null, null);
                var deliveryContext = new Mock<ConsumeContext<NotificationDeliveryReported>>();
                deliveryContext.SetupGet(item => item.Message).Returns(delivery);
                await new NotificationDeliveryConsumer(context, rabbitOptions).Consume(deliveryContext.Object);
                break;
            case "status":
                var status = new NotificationStatusChanged(Guid.NewGuid(), fixture.Options.ApplicationId,
                    Guid.NewGuid().ToString(), Guid.NewGuid(), "queued", 1, DateTimeOffset.UtcNow);
                var statusContext = new Mock<ConsumeContext<NotificationStatusChanged>>();
                statusContext.SetupGet(item => item.Message).Returns(status);
                await new NotificationResultConsumer(context, rabbitOptions).Consume(statusContext.Object);
                break;
            case "campaign":
                await new EmailCampaignService(context, fixture.CreateSender(context), Mock.Of<IEmailCampaignScheduler>(),
                    NullLogger<EmailCampaignService>.Instance).ProcessCampaignAsync(Guid.NewGuid());
                break;
        }

        Assert.True(transactions.HasDisposedTransaction);
        Assert.Null(context.Database.CurrentTransaction);
    }
}
