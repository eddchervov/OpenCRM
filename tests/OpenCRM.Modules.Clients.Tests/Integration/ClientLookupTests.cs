using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Clients.Tests.Integration;

[Collection(ClientsDatabaseCollection.Name)]
public sealed class ClientLookupTests(ClientsDatabaseFixture fixture)
{
    [Fact]
    public async Task LookupReturnsEightItemsAndNextPageAndEscapesWildcards()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var otherOrganization = await fixture.CreateOrganizationAsync(context, "Другое пространство");
        var matchingClients = Enumerable.Range(0, 10)
            .Select(index => ClientsDatabaseFixture.CreateClient(organization, $"Подбор {index:D2}"))
            .ToArray();
        var wildcardClient = ClientsDatabaseFixture.CreateClient(organization, "Тест %_ поиск");
        var phoneClient = ClientsDatabaseFixture.CreateClient(organization, "Телефонный клиент");
        phoneClient.Phone = "+7 (900) 123-45-67";
        var archivedClient = ClientsDatabaseFixture.CreateClient(organization, "Подбор архивный");
        archivedClient.Delete(DateTime.UtcNow);
        var foreignClient = ClientsDatabaseFixture.CreateClient(otherOrganization, "Подбор чужой");
        context.OrganizationClients.AddRange(matchingClients.Append(wildcardClient).Append(phoneClient).Append(archivedClient).Append(foreignClient));
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var query = services.GetRequiredService<IOrganizationClientQueryService>();

        var firstPage = await query.LookupAsync(organization.OwnerUserId, organization.Id, "Подбор", 1);
        var secondPage = await query.LookupAsync(organization.OwnerUserId, organization.Id, "Подбор", 2);
        var wildcardPage = await query.LookupAsync(organization.OwnerUserId, organization.Id, "%_", 1);
        var phonePage = await query.LookupAsync(organization.OwnerUserId, organization.Id, "9001", 1);
        var shortSearch = await query.LookupAsync(organization.OwnerUserId, organization.Id, "%", 1);
        var nameWithDigits = await query.LookupAsync(organization.OwnerUserId, organization.Id, "Подбор 00", 1);
        var clampedPage = await query.LookupAsync(organization.OwnerUserId, organization.Id, "Подбор", 0);
        var pageBeyondResults = await query.LookupAsync(organization.OwnerUserId, organization.Id, "Подбор", 1001);

        Assert.Equal(8, firstPage.Items.Count);
        Assert.Equal(10, firstPage.TotalCount);
        Assert.Equal(2, secondPage.Items.Count);
        Assert.Equal(10, secondPage.TotalCount);
        Assert.Equal(matchingClients.OrderBy(client => client.Name).Take(8).Select(client => client.Id), firstPage.Items.Select(item => item.Id));
        Assert.Equal(matchingClients.OrderBy(client => client.Name).Skip(8).Select(client => client.Id), secondPage.Items.Select(item => item.Id));
        Assert.Equal(firstPage.Items.Select(item => item.Id), clampedPage.Items.Select(item => item.Id));
        Assert.Empty(pageBeyondResults.Items);
        Assert.Equal(10, pageBeyondResults.TotalCount);
        Assert.Equal(wildcardClient.Id, Assert.Single(wildcardPage.Items).Id);
        Assert.Equal(phoneClient.Id, Assert.Single(phonePage.Items).Id);
        Assert.Empty(shortSearch.Items);
        Assert.Equal(0, shortSearch.TotalCount);
        Assert.Equal(matchingClients[0].Id, Assert.Single(nameWithDigits.Items).Id);
        await Assert.ThrowsAsync<ArgumentException>(() => query.LookupAsync(
            organization.OwnerUserId, organization.Id, new string('я', 101), 1));
    }

    [Theory]
    [InlineData("+7 (900) 123-45-67", "8 (900) 123-45-67")]
    [InlineData("8 (900) 123-45-67", "+7 (900) 123-45-67")]
    [InlineData("+7 (900) 123-45-67", "9001234567")]
    [InlineData("9001234567", "+7 (900) 123-45-67")]
    [InlineData("+7 (900) 123-45-67", "8900")]
    [InlineData("8 (900) 123-45-67", "7900")]
    public async Task LookupFindsPhoneAliasesWithoutExposingForeignOrDeletedClients(string storedPhone, string search)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var otherOrganization = await fixture.CreateOrganizationAsync(context);
        var existing = ClientsDatabaseFixture.CreateClient(organization, "Иван");
        existing.Phone = storedPhone;
        var foreign = ClientsDatabaseFixture.CreateClient(otherOrganization, "Иван чужой");
        foreign.Phone = storedPhone;
        var deleted = ClientsDatabaseFixture.CreateClient(organization, "Иван удалённый");
        deleted.Phone = storedPhone;
        deleted.Delete(DateTime.UtcNow);
        context.OrganizationClients.AddRange(existing, foreign, deleted);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);

        var result = await services.GetRequiredService<IOrganizationClientQueryService>()
            .LookupAsync(organization.OwnerUserId, organization.Id, search, 1);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(existing.Id, Assert.Single(result.Items).Id);
        Assert.Equal(storedPhone, result.Items[0].Phone);
    }

    [Theory]
    [InlineData("84901234567", "+84901234567", "+74901234567")]
    [InlineData("9890011233", "+9890011233", "+79890011233")]
    public async Task LookupPreservesExplicitInternationalCountryPrefix(
        string russianPhone,
        string internationalPhone,
        string canonicalRussianPhone)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var russianClient = ClientsDatabaseFixture.CreateClient(organization, "Российский контакт");
        russianClient.Phone = russianPhone;
        var canonicalRussianClient = ClientsDatabaseFixture.CreateClient(organization, "Российский контакт с кодом страны");
        canonicalRussianClient.Phone = canonicalRussianPhone;
        var internationalClient = ClientsDatabaseFixture.CreateClient(organization, "Международный контакт");
        internationalClient.Phone = internationalPhone;
        context.OrganizationClients.AddRange(russianClient, canonicalRussianClient, internationalClient);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var query = services.GetRequiredService<IOrganizationClientQueryService>();

        var russianResult = await query.LookupAsync(organization.OwnerUserId, organization.Id, russianPhone, 1);
        var internationalResult = await query.LookupAsync(organization.OwnerUserId, organization.Id, internationalPhone, 1);

        Assert.Equal(2, russianResult.TotalCount);
        Assert.Contains(russianResult.Items, item => item.Id == russianClient.Id);
        Assert.Contains(russianResult.Items, item => item.Id == canonicalRussianClient.Id);
        Assert.Equal(1, internationalResult.TotalCount);
        Assert.Equal(internationalClient.Id, Assert.Single(internationalResult.Items).Id);
        Assert.Equal(internationalPhone, internationalResult.Items[0].Phone);
    }

    [Fact]
    public async Task LookupChecksAccessEvenWhenSearchIsTooShort()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var subscription = new Mock<IOrganizationSubscriptionService>();
        subscription.Setup(service => service.HasFeatureAsync(
                organization.OwnerUserId, organization.Id, It.IsAny<string>()))
            .ReturnsAsync(true);
        subscription.Setup(service => service.EnsureWorkspaceCanReadAsync(
                organization.OwnerUserId, organization.Id, It.IsAny<string>()))
            .Returns(Task.FromException(new UnauthorizedAccessException()));
        using var services = fixture.CreateServices(context, subscriptionService: subscription.Object);
        var query = services.GetRequiredService<IOrganizationClientQueryService>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            query.LookupAsync(organization.OwnerUserId, organization.Id, "x", 1));

        subscription.Verify(service => service.EnsureWorkspaceCanReadAsync(
            organization.OwnerUserId, organization.Id, OrganizationPermissionCodes.ClientsView), Times.Once);
    }
}
