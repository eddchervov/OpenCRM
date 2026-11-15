using Microsoft.Extensions.Configuration;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Organizations;
using OpenCRM.Modules.Organizations.Queries;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[Collection(OrganizationsDatabaseCollection.Name)]
public sealed class OrganizationMemberNameTests(OrganizationsDatabaseFixture fixture)
{
    [Fact]
    public async Task MemberNamesFollowExplicitLinksInEachOrganizationIncludingInactiveEmployees()
    {
        await using var context = fixture.CreateContext();
        var firstOrganization = await fixture.CreateOrganizationAsync(context);
        var secondOrganization = await fixture.CreateOrganizationAsync(context);
        var user = new ApplicationUser { Email = "member-names@example.test", UserName ="member-names" };
        var firstMember = await fixture.AddMemberAsync(context, firstOrganization, user);
        var secondMember = await fixture.AddMemberAsync(context, secondOrganization, user);

        var firstEmployee = new OrganizationEmployee
        {
            Organization = firstOrganization,
            MemberId = firstMember.Id,
            Name = "Иван Петров"
        };

        context.OrganizationEmployees.AddRange(firstEmployee, new OrganizationEmployee
        {
            Organization = secondOrganization,
            MemberId = secondMember.Id,
            Name = "Иван Петрович",
            IsActive = false
        });

        await context.SaveChangesAsync();
        var permissions = new Mock<IOrganizationPermissionService>();
        var query = new OrganizationQueryService(context, permissions.Object, new OrganizationScenarioReader(context), new ConfigurationBuilder().Build());
        var firstResult = await query.GetByIdAsync(user.Id, firstOrganization.Id);

        Assert.Equal("Иван Петров", firstResult.Members.Single(member => member.UserId == user.Id).EmployeeName);
        Assert.Null(firstResult.Members.Single(member => member.UserId == firstOrganization.OwnerUserId).EmployeeName);
        var overview = await query.GetOverviewAsync(user.Id);
        Assert.Equal("Иван Петров", overview.Organizations.Single(item => item.Id == firstOrganization.Id)
            .Members.Single(member => member.UserId == user.Id).EmployeeName);
        Assert.Equal("Иван Петрович", overview.Organizations.Single(item => item.Id == secondOrganization.Id)
            .Members.Single(member => member.UserId == user.Id).EmployeeName);

        firstEmployee.Name = "Новое имя";
        await context.SaveChangesAsync();
        var renamedResult = await query.GetByIdAsync(user.Id, firstOrganization.Id);
        Assert.Equal("Новое имя", renamedResult.Members.Single(member => member.UserId == user.Id).EmployeeName);

        user.IsDeleted = true;
        await context.SaveChangesAsync();
        var deletedResult = await query.GetByIdAsync(firstOrganization.OwnerUserId, firstOrganization.Id);
        Assert.Null(deletedResult.Members.Single(member => member.UserId == user.Id).EmployeeName);

        user.IsDeleted = false;
        firstEmployee.MemberId = null;
        await context.SaveChangesAsync();
        var unlinkedResult = await query.GetByIdAsync(user.Id, firstOrganization.Id);
        Assert.Null(unlinkedResult.Members.Single(member => member.UserId == user.Id).EmployeeName);
        Assert.Equal(user.Email, unlinkedResult.Members.Single(member => member.UserId == user.Id).Email);
    }
}
