using System.Security.Claims;
using System.Text.Json;
using Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Testing;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Services.Interfaces.Export1C;
using OpenCRM.Services.Interfaces.Export1C.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.WebApp.Controllers.Api.V1.Organizations;

namespace OpenCRM.Modules.Workspace.Tests.Integration;

public sealed class Export1CApiTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>, IDisposable
{
    private readonly OpenCRMContext _context = fixture.CreateContext();
    private readonly Guid _organizationId = Guid.NewGuid();
    private readonly ApplicationUser _user = new();
    private readonly Mock<IOrganizationSubscriptionService> _subscriptions = new();
    private readonly Mock<IOrganizationPermissionService> _permissions = new();
    private readonly Mock<IExport1CService> _export = new();

    [Theory]
    [InlineData(false, "bp3", false)]
    [InlineData(false, "bp3", true)]
    [InlineData(false, "full", false)]
    [InlineData(false, "full", true)]
    [InlineData(true, "bp3", false)]
    [InlineData(true, "bp3", true)]
    [InlineData(true, "full", false)]
    [InlineData(true, "full", true)]
    public async Task OnlyBp3RequiresProBeforeReadingExportData(bool hasPro, string destination, bool download)
    {
        var controller = await this.CreateControllerAsync(hasPro);
        var options = Export1CApiTests.CreateOptions(destination);

        var result = download
            ? await controller.Download(this._organizationId, options)
            : await controller.Preview(this._organizationId, options);

        if (!hasPro && destination == "bp3")
        {
            Assert.IsType<ForbidResult>(result);
            this._export.Verify(service => service.CreateAsync(It.IsAny<Guid>(), It.IsAny<Export1COptionsDto>()), Times.Never);
            return;
        }

        if (download)
        {
            Assert.IsType<FileContentResult>(result);
        }
        else
        {
            Assert.IsType<OkObjectResult>(result);
        }

        this._export.Verify(service => service.CreateAsync(this._organizationId, options), Times.Once);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("", true)]
    [InlineData("other", false)]
    [InlineData("other", true)]
    public async Task UnknownDestinationIsRejectedWithoutReadingData(string destination, bool download)
    {
        var controller = await this.CreateControllerAsync(true);
        var options = Export1CApiTests.CreateOptions(destination);

        var result = download
            ? await controller.Download(this._organizationId, options)
            : await controller.Preview(this._organizationId, options);

        Assert.IsType<BadRequestObjectResult>(result);
        this._export.Verify(service => service.CreateAsync(It.IsAny<Guid>(), It.IsAny<Export1COptionsDto>()), Times.Never);
    }

    [Theory]
    [InlineData("bp3", false)]
    [InlineData("bp3", true)]
    [InlineData("full", false)]
    [InlineData("full", true)]
    public async Task ProDoesNotBypassRolePermissions(string destination, bool download)
    {
        var controller = await this.CreateControllerAsync(true);
        this._permissions.Setup(service => service.HasPermissionAsync(this._user.Id, this._organizationId, OrganizationPermissionCodes.DocumentsExport))
            .ReturnsAsync(false);
        var options = Export1CApiTests.CreateOptions(destination);

        var result = download
            ? await controller.Download(this._organizationId, options)
            : await controller.Preview(this._organizationId, options);

        Assert.IsType<ForbidResult>(result);
        this._export.Verify(service => service.CreateAsync(It.IsAny<Guid>(), It.IsAny<Export1COptionsDto>()), Times.Never);
    }

    [Fact]
    public async Task FreeCapabilitiesKeepUniversalExportAndSeparateBp3Availability()
    {
        var controller = await this.CreateControllerAsync(false);

        var result = Assert.IsType<OkObjectResult>(await controller.Capabilities(this._organizationId));
        var capabilities = JsonSerializer.SerializeToElement(result.Value);

        Assert.True(capabilities.GetProperty("CanExport").GetBoolean());
        Assert.False(capabilities.GetProperty("CanExportBp3").GetBoolean());
        this._export.Verify(service => service.CreateAsync(It.IsAny<Guid>(), It.IsAny<Export1COptionsDto>()), Times.Never);
        Assert.Equal("full", new Export1COptionsDto().DestinationCode);
    }

    [Fact]
    public async Task LegacyRequestWithoutDestinationKeepsFreeJsonExport()
    {
        var controller = await this.CreateControllerAsync(false);
        var options = new Export1COptionsDto
        {
            DateFrom = new DateOnly(2026, 10, 1),
            DateTo = new DateOnly(2026, 10, 29)
        };

        Assert.IsType<FileContentResult>(await controller.Download(this._organizationId, options));
        this._export.Verify(service => service.CreateAsync(this._organizationId, options), Times.Once);
    }

    [Theory]
    [InlineData("USD", false)]
    [InlineData("USD", true)]
    [InlineData("CNY", false)]
    [InlineData("CNY", true)]
    public async Task NonRubOrganizationCannotExport(string currencyCode, bool download)
    {
        var controller = await this.CreateControllerAsync(true, currencyCode);
        var options = Export1CApiTests.CreateOptions("full");

        var result = download
            ? await controller.Download(this._organizationId, options)
            : await controller.Preview(this._organizationId, options);

        Assert.IsType<ConflictObjectResult>(result);
        this._export.Verify(service => service.CreateAsync(It.IsAny<Guid>(), It.IsAny<Export1COptionsDto>()), Times.Never);
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("CNY")]
    public async Task NonRubOrganizationCannotReadExportCapabilities(string currencyCode)
    {
        var controller = await this.CreateControllerAsync(true, currencyCode);

        Assert.IsType<ConflictObjectResult>(await controller.Capabilities(this._organizationId));
        this._export.Verify(service => service.CreateAsync(It.IsAny<Guid>(), It.IsAny<Export1COptionsDto>()), Times.Never);
    }

    public void Dispose() => this._context.Dispose();

    private async Task<Export1CApiController> CreateControllerAsync(bool hasPro, string currencyCode = CurrencyCodes.Rub)
    {
        this._context.Organizations.Add(new Organization
        {
            Id = this._organizationId,
            Name = "Тестовая организация",
            OwnerUser = this._user,
            CurrencyCode = currencyCode
        });
        await this._context.SaveChangesAsync();

        var users = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), Options.Create(new IdentityOptions()),
            Mock.Of<IPasswordHasher<ApplicationUser>>(), Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(), Mock.Of<ILookupNormalizer>(),
            new IdentityErrorDescriber(), Mock.Of<IServiceProvider>(), Mock.Of<ILogger<UserManager<ApplicationUser>>>());
        users.Setup(service => service.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(this._user);
        this._subscriptions.Setup(service => service.GetWorkspaceAccessAsync(this._user.Id, this._organizationId))
            .ReturnsAsync(new OrganizationWorkspaceAccessDto { IsFreePlan = !hasPro, ExportsEnabled = true });
        this._subscriptions.Setup(service => service.HasFeatureAsync(this._user.Id, this._organizationId, It.IsAny<string>()))
            .ReturnsAsync((string _, Guid _, string code) => code == FeatureCodes.FinanceModule || code == FeatureCodes.FinanceExport1C && hasPro);
        this._permissions.Setup(service => service.HasPermissionAsync(this._user.Id, this._organizationId, It.IsAny<string>())).ReturnsAsync(true);
        this._export.Setup(service => service.CreateAsync(this._organizationId, It.IsAny<Export1COptionsDto>())).ReturnsAsync(new Export1CPackageDto());

        return new Export1CApiController(users.Object, this._subscriptions.Object, this._permissions.Object, this._export.Object,
            Mock.Of<ILogger<Export1CApiController>>(), this._context)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private static Export1COptionsDto CreateOptions(string destination) => new()
    {
        DestinationCode = destination,
        DateFrom = new DateOnly(2026, 10, 1),
        DateTo = new DateOnly(2026, 10, 29)
    };
}
