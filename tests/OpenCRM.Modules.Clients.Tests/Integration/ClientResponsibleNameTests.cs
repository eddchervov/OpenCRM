using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Modules.Clients.Contracts;

namespace OpenCRM.Modules.Clients.Tests.Integration;

[Collection(ClientsDatabaseCollection.Name)]
public sealed class ClientResponsibleNameTests(ClientsDatabaseFixture fixture)
{
    [Fact]
    public async Task ResponsibleNameUsesOnlyEmployeeLinkedInClientOrganizationAndFallsBackAfterUnlinking()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var otherOrganization = await fixture.CreateOrganizationAsync(context);

        var otherMember = new OrganizationMember
        {
            Organization = otherOrganization,
            User = organization.OwnerUser,
            RoleCode = OrganizationRoleCodes.Employee
        };

        context.OrganizationMembers.Add(otherMember);
        context.OrganizationEmployees.Add(new OrganizationEmployee
        {
            Organization = otherOrganization,
            MemberId = otherMember.Id,
            Name = "Имя в другой организации"
        });

        var client = ClientsDatabaseFixture.CreateClient(organization, "Клиент", organization.OwnerUserId);
        context.OrganizationClients.Add(client);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var query = services.GetRequiredService<IOrganizationClientQueryService>();
        var detailsQuery = services.GetRequiredService<IOrganizationClientDetailsQueryService>();
        var initialPage = await query.GetPageAsync(organization.OwnerUserId, organization.Id, null, null, 1, 10);
        Assert.Equal(organization.OwnerUser.Email, Assert.Single(initialPage.Items).ResponsibleDisplayName);

        var employee = new OrganizationEmployee
        {
            Organization = organization,
            MemberId = organization.Members.Single().Id,
            Name = "Иван Петров",
            IsActive = false
        };

        context.OrganizationEmployees.Add(employee);
        await context.SaveChangesAsync();
        var namedPage = await query.GetPageAsync(organization.OwnerUserId, organization.Id, null, null, 1, 10);
        var details = await detailsQuery.GetDetailsAsync(organization.OwnerUserId, organization.Id, client.Id);
        Assert.Equal("Иван Петров", Assert.Single(namedPage.Items).ResponsibleDisplayName);
        Assert.Equal("Иван Петров", details!.Client.ResponsibleDisplayName);

        organization.OwnerUser.IsDeleted = true;
        await context.SaveChangesAsync();
        var deletedDetails = await detailsQuery.GetDetailsAsync(organization.OwnerUserId, organization.Id, client.Id);
        Assert.Equal($"{organization.OwnerUser.Email} (удаленный пользователь)", deletedDetails!.Client.ResponsibleDisplayName);

        organization.OwnerUser.IsDeleted = false;
        employee.MemberId = null;
        await context.SaveChangesAsync();
        var unlinkedDetails = await detailsQuery.GetDetailsAsync(organization.OwnerUserId, organization.Id, client.Id);
        Assert.Equal(organization.OwnerUser.Email, unlinkedDetails!.Client.ResponsibleDisplayName);
    }
}
