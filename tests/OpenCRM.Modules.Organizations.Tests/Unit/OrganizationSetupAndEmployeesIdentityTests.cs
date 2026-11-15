using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.WebApp.Controllers.Api.V1.Organizations;

namespace OpenCRM.Modules.Organizations.Tests.Unit;

public sealed class OrganizationSetupAndEmployeesIdentityTests
{
    [Theory]
    [InlineData("UserID")]
    [InlineData(ClaimTypes.NameIdentifier)]
    [InlineData("sub")]
    public async Task SetupUsesSignedInUserForReadOpenAndSave(string claimType)
    {
        var organizationId = Guid.NewGuid();
        var service = new Mock<IOrganizationSetupService>(MockBehavior.Strict);
        var request = new SaveOrganizationSetupDto();
        service.Setup(value => value.GetSetupAsync("owner", organizationId)).ReturnsAsync(new OrganizationSetupDto());
        service.Setup(value => value.TryOpenSetupAsync("owner", organizationId)).ReturnsAsync(true);
        service.Setup(value => value.SaveSetupAsync("owner", organizationId, request)).Returns(Task.CompletedTask);

        var controller = new OrganizationSetupApiController(service.Object)
        {
            ControllerContext = CreateContext(claimType)
        };

        Assert.IsType<OkObjectResult>(await controller.Get(organizationId));
        Assert.IsType<OkObjectResult>(await controller.Open(organizationId));
        Assert.IsType<OkObjectResult>(await controller.Save(organizationId, request));
        service.Verify(value => value.GetSetupAsync("owner", organizationId), Times.Exactly(2));
        service.Verify(value => value.TryOpenSetupAsync("owner", organizationId), Times.Once);
        service.Verify(value => value.SaveSetupAsync("owner", organizationId, request), Times.Once);
    }

    [Theory]
    [InlineData("UserID")]
    [InlineData(ClaimTypes.NameIdentifier)]
    [InlineData("sub")]
    public async Task EmployeesUseSignedInUserForReadCreateAndUpdate(string claimType)
    {
        var organizationId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var service = new Mock<IOrganizationEmployeeService>(MockBehavior.Strict);
        var permissions = new Mock<IOrganizationPermissionService>(MockBehavior.Strict);
        var request = new OrganizationEmployeeDto { Name = "Мастер" };
        service.Setup(value => value.GetAsync("owner", organizationId)).ReturnsAsync(Array.Empty<OrganizationEmployeeDto>());
        service.Setup(value => value.SaveAsync("owner", organizationId, null, request)).ReturnsAsync(employeeId);
        service.Setup(value => value.SaveAsync("owner", organizationId, employeeId, request)).ReturnsAsync(employeeId);
        service.Setup(value => value.GetMemberOptionsAsync("owner", organizationId))
            .ReturnsAsync(Array.Empty<OrganizationEmployeeMemberOptionDto>());
        service.Setup(value => value.LinkMemberAsync("owner", organizationId, employeeId, It.IsAny<Guid?>(), 1))
            .Returns(Task.CompletedTask);
        permissions.Setup(value => value.HasPermissionAsync("owner", organizationId, OrganizationPermissionCodes.EmployeesManage))
            .ReturnsAsync(true);
        permissions.Setup(value => value.HasPermissionAsync("owner", organizationId, OrganizationPermissionCodes.MembersRolesManage))
            .ReturnsAsync(true);

        var controller = new OrganizationEmployeesApiController(
            service.Object,
            permissions.Object,
            Mock.Of<IOrganizationInvitationCommandService>())
        {
            ControllerContext = CreateContext(claimType)
        };

        Assert.IsType<OkObjectResult>(await controller.Get(organizationId));
        Assert.IsType<OkObjectResult>(await controller.Save(organizationId, null, request));
        Assert.IsType<OkObjectResult>(await controller.Save(organizationId, employeeId, request));
        Assert.IsType<NoContentResult>(await controller.LinkMember(
            organizationId, employeeId, new OrganizationEmployeesApiController.LinkEmployeeMemberDto(Guid.NewGuid(), 1)));
        service.VerifyAll();
        permissions.VerifyAll();
    }

    [Fact]
    public async Task MissingUserIdDoesNotReadOrChangeOrganization()
    {
        var organizationId = Guid.NewGuid();
        var setupService = new Mock<IOrganizationSetupService>(MockBehavior.Strict);
        var employees = new Mock<IOrganizationEmployeeService>(MockBehavior.Strict);
        var permissions = new Mock<IOrganizationPermissionService>(MockBehavior.Strict);
        var invitations = new Mock<IOrganizationInvitationCommandService>(MockBehavior.Strict);
        var setup = new OrganizationSetupApiController(setupService.Object)
        {
            ControllerContext = CreateContext(null)
        };

        var directory = new OrganizationEmployeesApiController(employees.Object, permissions.Object, invitations.Object)
        {
            ControllerContext = CreateContext(null)
        };

        Assert.IsType<UnauthorizedResult>(await setup.Get(organizationId));
        Assert.IsType<UnauthorizedResult>(await setup.Open(organizationId));
        Assert.IsType<UnauthorizedResult>(await setup.Save(organizationId, new SaveOrganizationSetupDto()));
        Assert.IsType<UnauthorizedResult>(await directory.Get(organizationId));
        Assert.IsType<UnauthorizedResult>(await directory.Save(organizationId, null, new OrganizationEmployeeDto()));
        Assert.IsType<UnauthorizedResult>(await directory.CreateWithInvitation(
            organizationId,
            new CreateOrganizationEmployeeInvitationDto()));
        Assert.IsType<UnauthorizedResult>(await directory.LinkMember(
            organizationId, Guid.NewGuid(), new OrganizationEmployeesApiController.LinkEmployeeMemberDto(null, 0)));
        setupService.VerifyNoOtherCalls();
        employees.VerifyNoOtherCalls();
        permissions.VerifyNoOtherCalls();
        invitations.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StaleEmployeeLinkVersionReturnsConflict()
    {
        var organizationId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var service = new Mock<IOrganizationEmployeeService>(MockBehavior.Strict);
        var permissions = new Mock<IOrganizationPermissionService>(MockBehavior.Strict);
        service.Setup(value => value.LinkMemberAsync("owner", organizationId, employeeId, null, 1))
            .ThrowsAsync(new DbUpdateConcurrencyException("Версия изменилась."));

        var controller = new OrganizationEmployeesApiController(
            service.Object,
            permissions.Object,
            Mock.Of<IOrganizationInvitationCommandService>())
        {
            ControllerContext = CreateContext(ClaimTypes.NameIdentifier)
        };

        Assert.IsType<ConflictObjectResult>(await controller.LinkMember(
            organizationId, employeeId, new OrganizationEmployeesApiController.LinkEmployeeMemberDto(null, 1)));
        service.VerifyAll();
        permissions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ConcurrentEmployeeLinkReturnsConflict()
    {
        var organizationId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var service = new Mock<IOrganizationEmployeeService>(MockBehavior.Strict);
        var permissions = new Mock<IOrganizationPermissionService>(MockBehavior.Strict);
        service.Setup(value => value.LinkMemberAsync("owner", organizationId, employeeId, memberId, 1))
            .ThrowsAsync(new DbUpdateException("Участник уже связан.", new Exception()));

        var controller = new OrganizationEmployeesApiController(
            service.Object,
            permissions.Object,
            Mock.Of<IOrganizationInvitationCommandService>())
        {
            ControllerContext = CreateContext(ClaimTypes.NameIdentifier)
        };

        Assert.IsType<ConflictObjectResult>(await controller.LinkMember(
            organizationId, employeeId, new OrganizationEmployeesApiController.LinkEmployeeMemberDto(memberId, 1)));
        service.VerifyAll();
        permissions.VerifyNoOtherCalls();
    }

    private static ControllerContext CreateContext(string? claimType)
    {
        var claims = claimType == null ? Array.Empty<Claim>() : [new Claim(claimType, "owner")];

        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
            }
        };
    }
}
