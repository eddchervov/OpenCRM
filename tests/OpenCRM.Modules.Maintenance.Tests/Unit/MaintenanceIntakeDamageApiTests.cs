using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.WebApp.Controllers.Api.V1.Maintenance;

namespace OpenCRM.Modules.Maintenance.Tests.Unit;

public sealed class MaintenanceIntakeDamageApiTests
{
    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task ConflictLogsOperationAndVersionsWithoutIntakeContents(string operation)
    {
        var organizationId = Guid.NewGuid();
        var maintenanceId = Guid.NewGuid();
        var damageId = Guid.NewGuid();
        const string UserId = "intake-test-user";
        var exception = new DbUpdateConcurrencyException("Осмотр обновлён после открытия.")
        {
            Data = { ["ExpectedVersion"] = 5u, ["ActualVersion"] = 6u }
        };

        var service = new Mock<IMaintenanceIntakeDamageService>(MockBehavior.Strict);
        service.Setup(value => value.SaveDamage(UserId, organizationId, maintenanceId, damageId,
                It.IsAny<SaveMaintenanceIntakeDamageDto>()))
            .ThrowsAsync(exception);
        service.Setup(value => value.DeleteDamage(UserId, organizationId, maintenanceId, damageId, 5u))
            .ThrowsAsync(exception);
        var logger = new Mock<ILogger<MaintenanceIntakeDamageApiController>>();

        var controller = new MaintenanceIntakeDamageApiController(service.Object, logger.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    TraceIdentifier = "intake-test-trace",
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, UserId)], "test"))
                }
            }
        };

        controller.Request.Method = operation;
        var result = operation == "PUT"
            ? await controller.SaveDamage(maintenanceId, damageId, organizationId,
                new SaveMaintenanceIntakeDamageDto { Version = 5u, Description = "Не включать в лог" })
            : await controller.DeleteDamage(maintenanceId, damageId, organizationId, 5u);

        Assert.IsType<ConflictObjectResult>(result);
        var log = Assert.Single(logger.Invocations, invocation => invocation.Method.Name == nameof(ILogger.Log));
        Assert.Equal(LogLevel.Warning, log.Arguments[0]);
        Assert.Same(exception, log.Arguments[3]);
        var values = Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, object?>>>(log.Arguments[2])
            .ToDictionary(value => value.Key, value => value.Value);
        Assert.Equal(operation, values["Operation"]);
        Assert.Equal(organizationId, values["OrganizationId"]);
        Assert.Equal(maintenanceId, values["MaintenanceId"]);
        Assert.Equal(damageId, values["DamageId"]);
        Assert.Equal(UserId, values["UserId"]);
        Assert.Equal(5u, values["ExpectedVersion"]);
        Assert.Equal(6u, values["ActualVersion"]);
        Assert.Equal("intake-test-trace", values["TraceId"]);
        Assert.DoesNotContain("Не включать в лог", log.Arguments[2].ToString());
    }
}
