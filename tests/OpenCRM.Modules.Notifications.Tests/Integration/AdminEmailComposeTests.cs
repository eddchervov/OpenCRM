using OpenCRM.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Notifications.Emails;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;
using OpenCRM.WebApp.Controllers.Admin.Api;

namespace OpenCRM.Modules.Notifications.Tests.Integration;

[Collection(nameof(NotificationDatabaseCollection))]
public sealed class AdminEmailComposeTests(NotificationFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ExternalRecipientUsesCampaignAndNotificationOutboxWithoutUserAccount()
    {
        await using var context = fixture.CreateContext();
        var sender = fixture.CreateSender(context);
        var campaigns = new EmailCampaignService(
            context, sender, Mock.Of<IEmailCampaignScheduler>(), NullLogger<EmailCampaignService>.Instance);
        var controller = new EmailMessagesApiController(
            campaigns,
            new AdminEmailCompositionService(context, campaigns),
            new AdminEmailTestOperationsService(context, sender),
            new AdminEmailJournalService(context))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var email = $"external-{Guid.NewGuid():N}@example.com";
        var request = new EmailCampaignScheduleRequest
        {
            TargetMode = "email", Email = " " + email + " ", OnlyConfirmedEmail = false,
            Subject = "Личное письмо", HtmlBody = "<p>Здравствуйте!</p>"
        };

        var preview = (await controller.PreviewRecipients(request)).Value!;
        Assert.Equal(email, Assert.Single(preview.Items).Email);
        Assert.Null(preview.Items[0].UserId);
        var scheduled = (await controller.ScheduleCampaign(request)).Value!;
        Assert.Equal(1, scheduled.ScheduledCount);
        await campaigns.ProcessCampaignAsync(scheduled.CampaignId);

        var message = await context.EmailMessages.SingleAsync(item => item.ToEmail == email);
        Assert.Equal("system/custom-email", message.TemplateKey);
        Assert.Equal(EmailMessageStatusCodes.Queued, message.StatusCode);
        Assert.NotNull(message.CampaignRecipientId);
        Assert.Null(message.UserId);
        Assert.Contains("htmlBody", message.TemplateDataJson!);
        Assert.False(await context.Users.AnyAsync(user => user.Email == email));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a@example.com,b@example.com")]
    [InlineData("a@example.com\r\nBcc:b@example.com")]
    public async Task InvalidExternalRecipientIsRejectedBeforeScheduling(string email)
    {
        await using var context = fixture.CreateContext();
        var campaigns = new Mock<IEmailCampaignService>(MockBehavior.Strict);
        var controller = new EmailMessagesApiController(
            campaigns.Object,
            new AdminEmailCompositionService(context, campaigns.Object),
            new AdminEmailTestOperationsService(context, fixture.CreateSender(context)),
            new AdminEmailJournalService(context))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var request = new EmailCampaignScheduleRequest
        {
            TargetMode = " email ", Email = email, OnlyConfirmedEmail = false,
            Subject = "Тема", HtmlBody = "<p>Текст</p>"
        };

        Assert.IsType<BadRequestObjectResult>((await controller.PreviewRecipients(request)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.ScheduleCampaign(request)).Result);
        campaigns.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UnconfirmedUserCanBeSelectedAndFoundOutsideFirstPage()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser
        {
            Email = $"old-{Guid.NewGuid():N}@example.com", UserName ="Старый пользователь",
            DatetimeCreation = DateTime.UtcNow.AddYears(-1)
        };
        context.Users.Add(user);
        for (var index = 0; index < 31; index++)
        {
            context.Users.Add(new ApplicationUser
            {
                Email = $"new-{Guid.NewGuid():N}@example.com", DatetimeCreation = DateTime.UtcNow
            });
        }

        await context.SaveChangesAsync();
        var campaigns = Mock.Of<IEmailCampaignService>();
        var controller = new EmailMessagesApiController(
            campaigns,
            new AdminEmailCompositionService(context, campaigns),
            new AdminEmailTestOperationsService(context, fixture.CreateSender(context)),
            new AdminEmailJournalService(context));
        Assert.DoesNotContain((await controller.GetComposeData()).Value!.Users, candidate => candidate.Id == user.Id);
        Assert.Equal(user.Id, Assert.Single((await controller.GetComposeData(user.Email)).Value!.Users).Id);
        var request = new EmailCampaignRecipientsRequest
        {
            TargetMode = "user", UserId = user.Id, OnlyConfirmedEmail = false
        };
        Assert.Equal(user.Email, Assert.Single((await controller.PreviewRecipients(request)).Value!.Items).Email);
    }
}
