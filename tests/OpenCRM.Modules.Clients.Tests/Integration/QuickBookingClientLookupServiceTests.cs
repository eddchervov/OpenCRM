using Moq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Clients.Queries;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Clients.Tests.Integration;

[Collection(ClientsDatabaseCollection.Name)]
public sealed class QuickBookingClientLookupServiceTests(ClientsDatabaseFixture fixture)
{
    [Fact]
    public async Task LookupIdentifiesOnlyByPhoneAndExcludesClientsOutsideOrganizationOrSoftDeleted()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var otherOrganization = await fixture.CreateOrganizationAsync(context, "Другое пространство");
        var phoneClient = ClientsDatabaseFixture.CreateClient(organization, "Клиент по телефону");
        phoneClient.Phone = "+7 (900) 111-22-33";
        var nondigitPhoneClient = ClientsDatabaseFixture.CreateClient(organization, "Клиент по буквам в телефоне");
        nondigitPhoneClient.Phone = "abc";
        var nameClient = ClientsDatabaseFixture.CreateClient(organization, "Клиент по имени");
        var deletedClient = ClientsDatabaseFixture.CreateClient(organization, "Удалённый клиент");
        deletedClient.Phone = "79001112233";
        deletedClient.Delete(DateTime.UtcNow);
        var foreignClient = ClientsDatabaseFixture.CreateClient(otherOrganization, "Чужой клиент");
        foreignClient.Phone = "79001112233";
        context.OrganizationClients.AddRange(phoneClient, nondigitPhoneClient, nameClient, deletedClient, foreignClient);
        await context.SaveChangesAsync();
        var subscriptions = this.CreateSubscriptions();
        var lookup = new QuickBookingClientLookupService(context, subscriptions.Object);

        var phoneMatch = await lookup.FindExistingAsync(
            organization.OwnerUserId,
            organization.Id,
            "7 900 1112233",
            "Клиент по имени");
        var nameMatch = await lookup.FindExistingAsync(
            organization.OwnerUserId,
            organization.Id,
            null,
            "  КЛИЕНТ ПО ИМЕНИ  ");
        var nondigitPhoneMatch = await lookup.FindExistingAsync(
            organization.OwnerUserId,
            organization.Id,
            "+",
            "Клиент по имени");

        Assert.Equal(phoneClient.Id, phoneMatch);
        Assert.Null(nameMatch);
        Assert.Null(nondigitPhoneMatch);
        Assert.True(await lookup.ExistsAsync(organization.OwnerUserId, organization.Id, phoneClient.Id));
        Assert.False(await lookup.ExistsAsync(organization.OwnerUserId, organization.Id, deletedClient.Id));
        Assert.False(await lookup.ExistsAsync(organization.OwnerUserId, organization.Id, foreignClient.Id));
        subscriptions.Verify(service => service.EnsureWorkspaceCanWriteAsync(
            organization.OwnerUserId,
            organization.Id,
            OrganizationPermissionCodes.MaintenanceCreate), Times.Exactly(6));
        subscriptions.Verify(service => service.EnsureWorkspaceCanReadAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
        subscriptions.Verify(service => service.HasFeatureAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task LookupPropagatesWorkspaceWriteDenial()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var subscriptions = this.CreateSubscriptions();
        subscriptions.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                organization.OwnerUserId,
                organization.Id,
                OrganizationPermissionCodes.MaintenanceCreate))
            .ThrowsAsync(new InvalidOperationException("Недостаточно прав."));
        var lookup = new QuickBookingClientLookupService(context, subscriptions.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => lookup.ExistsAsync(
            organization.OwnerUserId,
            organization.Id,
            Guid.NewGuid()));
    }

    [Fact]
    public async Task DifferentPhonesAndSameNameDoNotIdentifyAnotherClientWhileNameCanSuggestDuplicate()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var existing = ClientsDatabaseFixture.CreateClient(organization, "Иван");
        existing.Phone = "+7 (900) 111-22-33";
        context.OrganizationClients.Add(existing);
        await context.SaveChangesAsync();
        var lookup = new QuickBookingClientLookupService(context, this.CreateSubscriptions().Object);

        Assert.Null(await lookup.FindExistingAsync(organization.OwnerUserId, organization.Id, "+7 900 777-88-99", "Иван"));
        Assert.Null(await lookup.FindExistingAsync(organization.OwnerUserId, organization.Id, null, "Иван"));
        Assert.Equal(existing.Id, await lookup.FindPotentialDuplicateAsync(organization.OwnerUserId, organization.Id, null, " иВаН "));
    }

    [Theory]
    [InlineData("+7 (900) 123-45-67", "8 900 1234567")]
    [InlineData("8 (900) 123-45-67", "+7 900 1234567")]
    [InlineData("9001234567", "+7 (900) 123-45-67")]
    [InlineData("+7 900 1234567", "9001234567")]
    [InlineData("+49 (30) 123-45-67", "+49 30 1234567")]
    public async Task EquivalentPhoneFormatsIdentifySingleClient(string storedPhone, string enteredPhone)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var existing = ClientsDatabaseFixture.CreateClient(organization, "Контакт");
        existing.Phone = storedPhone;
        context.OrganizationClients.Add(existing);
        await context.SaveChangesAsync();
        var lookup = new QuickBookingClientLookupService(context, this.CreateSubscriptions().Object);

        Assert.Equal(existing.Id, await lookup.FindExistingAsync(organization.OwnerUserId, organization.Id, enteredPhone, "Другое имя"));
        Assert.Equal(storedPhone, existing.Phone);
    }

    [Theory]
    [InlineData("84901234567", "+84901234567")]
    [InlineData("9890011233", "+9890011233")]
    public async Task ExplicitCountryPrefixDoesNotIdentifyRussianTrunkOrMobileNumber(string russianPhone, string internationalPhone)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var russianClient = ClientsDatabaseFixture.CreateClient(organization, "Российский контакт");
        russianClient.Phone = russianPhone;
        var internationalClient = ClientsDatabaseFixture.CreateClient(organization, "Международный контакт");
        internationalClient.Phone = internationalPhone;
        context.OrganizationClients.AddRange(russianClient, internationalClient);
        await context.SaveChangesAsync();
        var lookup = new QuickBookingClientLookupService(context, this.CreateSubscriptions().Object);

        Assert.Equal(russianClient.Id, await lookup.FindExistingAsync(organization.OwnerUserId, organization.Id, russianPhone, null));
        Assert.Equal(internationalClient.Id, await lookup.FindExistingAsync(organization.OwnerUserId, organization.Id, internationalPhone, null));
    }

    [Fact]
    public async Task SharedPhoneRequiresExplicitClientChoice()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var first = ClientsDatabaseFixture.CreateClient(organization, "Первый клиент");
        first.Phone = "+7 (900) 123-45-67";
        var second = ClientsDatabaseFixture.CreateClient(organization, "Второй клиент");
        second.Phone = "8 900 1234567";
        context.OrganizationClients.AddRange(first, second);
        await context.SaveChangesAsync();
        var lookup = new QuickBookingClientLookupService(context, this.CreateSubscriptions().Object);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => lookup.FindExistingAsync(
            organization.OwnerUserId, organization.Id, "9001234567", first.Name));

        Assert.Contains("Выберите клиента из списка", error.Message);
    }

    [Theory]
    [InlineData("+")]
    [InlineData("abcdef")]
    [InlineData("123456")]
    [InlineData("1234567890123456")]
    public async Task InvalidNewPhoneIsRejectedBeforeClientCreation(string phone)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            services.GetRequiredService<IOrganizationClientCommandService>().ResolveQuickBookingClientAsync(
                organization.OwnerUserId, organization.Id, phone, "Иван", "QA"));

        Assert.Contains("корректный номер телефона", error.Message);
        Assert.False(await context.OrganizationClients.AnyAsync(client => client.OrganizationId == organization.Id));
    }

    private Mock<IOrganizationSubscriptionService> CreateSubscriptions()
    {
        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), OrganizationPermissionCodes.MaintenanceCreate))
            .Returns(Task.CompletedTask);

        return subscriptions;
    }
}
