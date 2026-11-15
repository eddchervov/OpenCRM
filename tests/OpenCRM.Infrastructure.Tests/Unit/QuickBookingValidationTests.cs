using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

using Moq;

using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.WebApp.Infrastructure.Api;

namespace OpenCRM.Infrastructure.Tests.Unit;

public sealed class QuickBookingValidationTests
{
    [Fact]
    public void DetailingRequestAcceptsCustomWorkBeforeCatalogService()
    {
        const string json = """
            {
              "registrationPlate": "А667РО142",
              "plannedDate": "2026-09-17",
              "services": [
                { "name": "Полировка капота", "price": 900, "quantity": 1 },
                {
                  "name": "Экспресс-мойка",
                  "description": null,
                  "catalogItemId": "83e146c4-e1bd-43bf-9de5-3d5242d73ab0",
                  "price": 1200,
                  "quantity": 1
                }
              ]
            }
            """;

        var request = JsonSerializer.Deserialize<CreateOrganizationDetailingQuickBookingDto>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(request);
        Assert.Null(request.ServiceId);
        Assert.Collection(
            request.Services!,
            customWork =>
            {
                Assert.Equal("Полировка капота", customWork.Name);
                Assert.Null(customWork.Description);
                Assert.Null(customWork.CatalogItemId);
            },
            catalogService => Assert.NotNull(catalogService.CatalogItemId));
    }

    [Fact]
    public void ApiValidationFailureIsLoggedAndNormalized()
    {
        var logger = new Mock<ILogger<ApiErrorResultFilter>>();
        var filter = new ApiErrorResultFilter(logger.Object);
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "trace-validation"
        };
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.Path = "/api/v1/organizations/test/detailing/bookings/quick";
        var validationProblem = new ValidationProblemDetails(new Dictionary<string, string[]>
        {
            ["Services[0].Description"] = ["The Description field is required."]
        });
        var result = new ObjectResult(validationProblem)
        {
            StatusCode = StatusCodes.Status400BadRequest
        };
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());
        var resultContext = new ResultExecutingContext(
            actionContext,
            [],
            result,
            new object());

        filter.OnResultExecuting(resultContext);

        var response = Assert.IsType<ApiErrorResponse>(result.Value);
        Assert.Equal("trace-validation", response.TraceId);
        Assert.Equal("Проверьте данные формы.", response.Message);
        Assert.Contains("Services[0].Description", response.Errors!.Keys);
        logger.Verify(
            value => value.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("trace-validation")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}