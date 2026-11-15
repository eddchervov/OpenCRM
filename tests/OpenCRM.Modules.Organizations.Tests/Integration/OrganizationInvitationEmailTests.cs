using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Emails;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[Collection(OrganizationsDatabaseCollection.Name)]
public sealed class OrganizationInvitationEmailTests(OrganizationsDatabaseFixture fixture)
{
    [Fact]
    public async Task UnregisteredInviteQueuesEscapedBrandedEmailAndRequiresVerifiedAddress()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context, "Сервис <Гараж>");
        var ownerId = organization.OwnerUserId;
        var subscriptions = OrganizationInvitationTestServices.CreateSubscription();
        var service = fixture.CreateInvitationCommandService(
            context,
            subscriptions.Object,
            configuration: new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AppSettings:BaseUrl"] = "https://example.com/"
                })
                .Build());
        var request = new InviteOrganizationMemberDto
        {
            Email = "new-person@example.test",
            RoleCode = OrganizationRoleCodes.Performer
        };

        await service.InviteMemberAsync(ownerId, organization.Id, request);

        var invitation = await context.OrganizationInvitations.SingleAsync(item => item.OrganizationId == organization.Id);
        var message = await context.EmailMessages.SingleAsync(item => item.ToEmail == request.Email);
        Assert.Equal(OrganizationInvitationStatusCodes.Pending, invitation.StatusCode);
        Assert.Equal("system/custom-email", message.TemplateKey);
        Assert.Equal(EmailMessageStatusCodes.Queued, message.StatusCode);
        Assert.Contains(invitation.Token, message.NotificationKey);
        using (var templateData = JsonDocument.Parse(message.TemplateDataJson!))
        {
            var html = templateData.RootElement.GetProperty("htmlBody").GetString();
            Assert.Contains(HtmlEncoder.Default.Encode(organization.Name), html);
            Assert.Contains(
                "https://example.com/auth/emailcodelogin?returnUrl=%2Forganizations",
                html);
            Assert.DoesNotContain("Сервис <Гараж>", html);
        }

        var invitee = new ApplicationUser { Email = request.Email };
        context.ApplicationUsers.Add(invitee);
        await context.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AcceptInvitationAsync(invitee.Id, invitation.Id));
        Assert.Contains("Подтвердите email", error.Message);
        Assert.False(await context.OrganizationMembers.AnyAsync(
            item => item.OrganizationId == organization.Id && item.UserId == invitee.Id));

        invitee = await context.ApplicationUsers.SingleAsync(value => value.Id == invitee.Id);
        invitee.EmailConfirmed = true;
        await context.SaveChangesAsync();
        await service.AcceptInvitationAsync(invitee.Id, invitation.Id);

        Assert.True(await context.OrganizationMembers.AnyAsync(
            item => item.OrganizationId == organization.Id && item.UserId == invitee.Id));
    }

    [Fact]
    public async Task InvitationEmailUsesDefaultBaseUrlWhenConfigurationIsMissing()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var service = fixture.CreateInvitationCommandService(context);

        await service.InviteMemberAsync(organization.OwnerUserId, organization.Id, new InviteOrganizationMemberDto
        {
            Email = "default-url@example.test",
            RoleCode = OrganizationRoleCodes.Employee
        });

        var email = await context.EmailMessages.SingleAsync(message => message.ToEmail == "default-url@example.test");
        using var data = JsonDocument.Parse(email.TemplateDataJson!);
        Assert.Contains(
            "https://opencrm.example.invalid/auth/emailcodelogin?returnUrl=%2Forganizations",
            data.RootElement.GetProperty("htmlBody").GetString());
    }
}
