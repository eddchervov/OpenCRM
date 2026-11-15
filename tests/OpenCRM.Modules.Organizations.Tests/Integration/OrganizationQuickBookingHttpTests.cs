using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Infrastructure.Common;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Catalog;
using OpenCRM.Modules.Clients;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Clients.Contracts.Dtos;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Modules.Maintenance;
using OpenCRM.Modules.Organizations;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Modules.Planning.Contracts;
using OpenCRM.Modules.Vehicles;
using OpenCRM.Services.Interfaces.Common;
using OpenCRM.Services.Interfaces.Features;
using OpenCRM.Services.Interfaces.Features.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.WebApp.Controllers.Api.V1.Organizations;
using OpenCRMSystemClock = OpenCRM.Domain.Interfaces.ISystemClock;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[Collection(OrganizationsDatabaseCollection.Name)]
public sealed class OrganizationQuickBookingHttpTests(OrganizationsDatabaseFixture fixture)
{
    private static readonly DateTime s_bookingDate = new(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task DetailingQuickBookingCreatesClientVehicleAndPlannedMaintenanceAndReusesNormalizedPlate()
    {
        await using var seed = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seed);
        using var host = this.CreateHost();
        using var client = host.GetTestClient();
        using var anonymous = await client.PostAsJsonAsync(DetailingRoute(organization.Id), new { });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);

        using var created = await client.PostAsJsonAsync(DetailingRoute(organization.Id), new
        {
            registrationPlate = "а123вс 54",
            clientName = "Новый клиент",
            clientPhone = "+7 900 123-45-67",
            manualBrandName = "Лада",
            manualModelName = "Веста",
            plannedDate = s_bookingDate,
            services = new[] { new { name = "Полировка", quantity = 1 } }
        });

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var response = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var result = response.RootElement;
        var clientId = result.GetProperty("organizationClientId").GetGuid();
        var vehicleId = result.GetProperty("vehicleId").GetGuid();
        var maintenanceId = result.GetProperty("maintenanceId").GetGuid();
        Assert.True(result.GetProperty("clientCreated").GetBoolean());
        Assert.True(result.GetProperty("vehicleCreated").GetBoolean());

        await using var verify = fixture.CreateContext();
        var savedClient = await verify.OrganizationClients.AsNoTracking().SingleAsync(value => value.Id == clientId);
        var savedVehicle = await verify.Vehicles.AsNoTracking().SingleAsync(value => value.Id == vehicleId);
        var savedMaintenance = await verify.Maintenance.AsNoTracking().Include(value => value.Vehicle).Include(value => value.Details)
            .SingleAsync(value => value.Id == maintenanceId);
        Assert.Equal(organization.Id, savedClient.OrganizationId);
        Assert.Equal("person", savedClient.PartyTypeCode);
        Assert.Equal(organization.Id, savedVehicle.OrganizationId);
        Assert.Equal(clientId, savedVehicle.OrganizationClientId);
        Assert.Equal("А123ВС54", savedVehicle.RegistrationPlate);
        Assert.Equal(MaintenanceStatus.Planned, savedMaintenance.Status);
        Assert.Equal<DateTime?>(s_bookingDate, savedMaintenance.PlannedDate);
        Assert.Equal("Полировка", Assert.Single(savedMaintenance.Details).Name);
        Assert.Single(await verify.OrganizationClientHistoryEvents.AsNoTracking()
            .Where(value => value.OrganizationId == organization.Id && value.OrganizationClientId == clientId).ToListAsync());
        Assert.Single(await verify.VehicleHistoryEvents.AsNoTracking()
            .Where(value => value.OrganizationId == organization.Id && value.VehicleId == vehicleId).ToListAsync());
        Assert.Single(await verify.MaintenanceHistoryEvents.AsNoTracking()
            .Where(value => value.OrganizationId == organization.Id && value.MaintenanceId == maintenanceId).ToListAsync());
        Assert.Contains(await verify.OrganizationAuditEvents.AsNoTracking()
            .Where(value => value.OrganizationId == organization.Id).ToListAsync(), value => value.TargetId == clientId.ToString("D"));

        using var reused = await client.PostAsJsonAsync(DetailingRoute(organization.Id), new
        {
            registrationPlate = "A 123 BC 54",
            organizationClientId = clientId,
            plannedDate = s_bookingDate.AddDays(1),
            services = new[] { new { name = "Повторный визит", quantity = 1 } }
        });

        Assert.Equal(HttpStatusCode.OK, reused.StatusCode);
        using var reusedResponse = JsonDocument.Parse(await reused.Content.ReadAsStringAsync());
        Assert.Equal(vehicleId, reusedResponse.RootElement.GetProperty("vehicleId").GetGuid());
        Assert.False(reusedResponse.RootElement.GetProperty("vehicleCreated").GetBoolean());
        Assert.False(reusedResponse.RootElement.GetProperty("clientCreated").GetBoolean());
        Assert.Equal(1, await verify.Vehicles.AsNoTracking().CountAsync(value => value.OrganizationId == organization.Id));
        Assert.Equal(1, await verify.OrganizationClients.AsNoTracking().CountAsync(value => value.OrganizationId == organization.Id));
    }

    [Theory]
    [InlineData("detailing", "simple")]
    [InlineData("detailing", "staged")]
    [InlineData("service-station", "simple")]
    [InlineData("service-station", "staged")]
    public async Task InquiryBookingCreatesPlannedMaintenanceAndRetriesLostResponseIdempotently(
        string scenarioCode,
        string workflowMode)
    {
        await using var seed = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seed);
        organization.PrimaryScenarioCode = scenarioCode;
        organization.MaintenanceWorkflowMode = workflowMode;
        var inquiry = new OrganizationInquiry
        {
            OrganizationId = organization.Id,
            ContactName = "Контакт обращения",
            Phone = "+7 900 123-45-67",
            Interest = "Полировка кузова",
            CreatedByUserId = organization.OwnerUserId
        };
        seed.OrganizationInquiries.Add(inquiry);
        await seed.SaveChangesAsync();

        using var host = this.CreateHost();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);
        var route = InquiryBookingRoute(organization.Id, inquiry.Id);
        var plate = "А123ВС000";
        var request = new CreateOrganizationInquiryBookingDto
        {
            Version = inquiry.Version,
            NewClient = new CreateOrganizationClientDto { Name = "Новый клиент", Phone = "+7 999 222-33-44" }
        };
        if (scenarioCode == "detailing")
        {
            request.Detailing = new CreateOrganizationDetailingQuickBookingDto
            {
                RegistrationPlate = plate,
                PlannedDate = s_bookingDate,
                PlannedStartTime = new TimeSpan(9, 0, 0),
                PlannedEndTime = new TimeSpan(10, 0, 0),
                Services =
                [
                    new OpenCRM.Modules.Maintenance.Contracts.Dtos.CreateMaintenanceDetailDto
                    {
                        Name = "Полировка",
                        Quantity = 1
                    }
                ]
            };
        }
        else
        {
            request.AutoService = new CreateOrganizationAutoServiceQuickBookingDto
            {
                RegistrationPlate = plate,
                PlannedDate = s_bookingDate,
                PlannedStartTime = new TimeSpan(9, 0, 0),
                PlannedEndTime = new TimeSpan(10, 0, 0),
                Services =
                [
                    new OpenCRM.Modules.Maintenance.Contracts.Dtos.CreateMaintenanceDetailDto
                    {
                        Name = "Диагностика",
                        Quantity = 1
                    }
                ]
            };
        }

        var concurrentResults = await Task.WhenAll(
            client.PostAsJsonAsync(route, request),
            client.PostAsJsonAsync(route, request));
        Guid maintenanceId;
        try
        {
            foreach (var result in concurrentResults)
            {
                Assert.True(result.StatusCode == HttpStatusCode.OK,
                    $"{result.StatusCode}: {await result.Content.ReadAsStringAsync()}");
            }
            using var firstResponse = JsonDocument.Parse(await concurrentResults[0].Content.ReadAsStringAsync());
            using var secondResponse = JsonDocument.Parse(await concurrentResults[1].Content.ReadAsStringAsync());
            maintenanceId = firstResponse.RootElement.GetProperty("maintenanceId").GetGuid();
            Assert.Equal(maintenanceId, secondResponse.RootElement.GetProperty("maintenanceId").GetGuid());
        }
        finally
        {
            foreach (var result in concurrentResults)
            {
                result.Dispose();
            }
        }

        using var retried = await client.PostAsJsonAsync(route, request);
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        using var retryResponse = JsonDocument.Parse(await retried.Content.ReadAsStringAsync());
        Assert.Equal(maintenanceId, retryResponse.RootElement.GetProperty("maintenanceId").GetGuid());

        await using var verify = fixture.CreateContext();
        var savedInquiry = await verify.OrganizationInquiries.AsNoTracking().SingleAsync(item => item.Id == inquiry.Id);
        var savedMaintenance = await verify.Maintenance.AsNoTracking().SingleAsync(item => item.Id == maintenanceId);
        Assert.Equal(OrganizationInquiryStageCodes.Booked, savedInquiry.StageCode);
        Assert.Equal(maintenanceId, savedInquiry.MaintenanceId);
        Assert.Equal(MaintenanceStatus.Planned, savedMaintenance.Status);
        Assert.Equal(1, await verify.Maintenance.CountAsync(item => item.Vehicle.OrganizationId == organization.Id));
        Assert.Equal(1, await verify.OrganizationClients.CountAsync(item => item.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task InquiryBookingPreservesExplicitVehicleWhenPlateIsAmbiguous()
    {
        await using var seed = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seed);
        organization.PrimaryScenarioCode = "detailing";
        var first = new Vehicle
        {
            UserId = organization.OwnerUserId,
            OrganizationId = organization.Id,
            RegistrationPlate = "А123ВС000"
        };
        var selected = new Vehicle
        {
            UserId = organization.OwnerUserId,
            OrganizationId = organization.Id,
            RegistrationPlate = first.RegistrationPlate
        };
        var inquiry = new OrganizationInquiry
        {
            OrganizationId = organization.Id,
            ContactName = "Звонок без контакта",
            Interest = "Мойка",
            CreatedByUserId = organization.OwnerUserId
        };
        seed.Vehicles.AddRange(first, selected);
        seed.OrganizationInquiries.Add(inquiry);
        await seed.SaveChangesAsync();
        using var host = this.CreateHost();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);

        using var response = await client.PostAsJsonAsync(InquiryBookingRoute(organization.Id, inquiry.Id), new
        {
            version = inquiry.Version,
            detailing = new
            {
                vehicleId = selected.Id,
                registrationPlate = selected.RegistrationPlate,
                plannedDate = s_bookingDate,
                services = new[] { new { name = "Мойка", quantity = 1 } }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var maintenanceId = json.RootElement.GetProperty("maintenanceId").GetGuid();
        await using var verify = fixture.CreateContext();
        Assert.Equal(selected.Id, await verify.Maintenance.Where(value => value.Id == maintenanceId)
            .Select(value => value.VehicleId).SingleAsync());
        Assert.Equal(2, await verify.Vehicles.CountAsync(value => value.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task InquiryBookingRejectsContactAutomergeAndRollsBack()
    {
        await using var seed = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seed);
        var inquiry = new OrganizationInquiry
        {
            OrganizationId = organization.Id,
            ContactName = "Контакт обращения",
            Phone = "+7 900 123-45-67",
            Interest = "Полировка кузова",
            CreatedByUserId = organization.OwnerUserId
        };
        seed.OrganizationInquiries.Add(inquiry);
        await seed.SaveChangesAsync();

        using var host = this.CreateHost();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);
        using var response = await client.PostAsJsonAsync(InquiryBookingRoute(organization.Id, inquiry.Id), new
        {
            version = inquiry.Version,
            detailing = new
            {
                registrationPlate = "А123ВС54",
                clientName = inquiry.ContactName,
                clientPhone = inquiry.Phone,
                plannedDate = s_bookingDate,
                services = new[] { new { name = "Полировка", quantity = 1 } }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var verify = fixture.CreateContext();
        Assert.Equal(0, await verify.Maintenance.CountAsync(item => item.Vehicle.OrganizationId == organization.Id));
        Assert.Equal(0, await verify.OrganizationClients.CountAsync(item => item.OrganizationId == organization.Id));
        Assert.Equal(OrganizationInquiryStageCodes.New,
            (await verify.OrganizationInquiries.AsNoTracking().SingleAsync(item => item.Id == inquiry.Id)).StageCode);
    }

    [Fact]
    public async Task InquiryBookingWithoutClientDoesNotCreatePlaceholderClient()
    {
        await using var seed = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seed);
        var inquiry = new OrganizationInquiry
        {
            OrganizationId = organization.Id,
            ContactName = "Контакт без клиента",
            Interest = "Консультация",
            CreatedByUserId = organization.OwnerUserId
        };
        seed.OrganizationInquiries.Add(inquiry);
        await seed.SaveChangesAsync();

        using var host = this.CreateHost();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);
        using var response = await client.PostAsJsonAsync(InquiryBookingRoute(organization.Id, inquiry.Id), new
        {
            version = inquiry.Version,
            detailing = new
            {
                registrationPlate = "А321ВС54",
                plannedDate = s_bookingDate,
                services = new[] { new { name = "Полировка", quantity = 1 } }
            }
        });

        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        await using var verify = fixture.CreateContext();
        Assert.Equal(0, await verify.OrganizationClients.CountAsync(item => item.OrganizationId == organization.Id));
        Assert.Equal(1, await verify.Maintenance.CountAsync(item => item.Vehicle.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task InquiryBookingCommitRetryUsesUnmodifiedRequestAndCreatesOnePlannedBooking()
    {
        await using var seed = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seed);
        var inquiry = new OrganizationInquiry
        {
            OrganizationId = organization.Id,
            ContactName = "Контакт обращения",
            Interest = "Полировка кузова",
            CreatedByUserId = organization.OwnerUserId
        };
        seed.OrganizationInquiries.Add(inquiry);
        await seed.SaveChangesAsync();

        var failure = new OrganizationCommitFailureInterceptor(failAfterCommit: false);
        using var host = this.CreateHost(failure);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);
        failure.Arm();
        using var response = await client.PostAsJsonAsync(InquiryBookingRoute(organization.Id, inquiry.Id), new
        {
            version = inquiry.Version,
            newClient = new { name = "Клиент с повтором", phone = "+7 999 333-44-55" },
            detailing = new
            {
                registrationPlate = "А654ВС54",
                plannedDate = s_bookingDate,
                isPlannedBooking = false,
                services = new[] { new { name = "Полировка", quantity = 1 } }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, failure.Failures);
        Assert.Equal(2, failure.CommitAttempts);
        await using var verify = fixture.CreateContext();
        Assert.Equal(1, await verify.OrganizationClients.CountAsync(item => item.OrganizationId == organization.Id));
        Assert.Equal(1, await verify.Maintenance.CountAsync(item => item.Vehicle.OrganizationId == organization.Id));
        Assert.Equal(MaintenanceStatus.Planned,
            await verify.Maintenance.Where(item => item.Vehicle.OrganizationId == organization.Id)
                .Select(item => item.Status).SingleAsync());
        Assert.Equal(OrganizationInquiryStageCodes.Booked,
            (await verify.OrganizationInquiries.AsNoTracking().SingleAsync(item => item.Id == inquiry.Id)).StageCode);
    }

    [Fact]
    public async Task InquiryBookingRollsBackExplicitClientWhenBookingValidationFails()
    {
        await using var seed = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seed);
        var inquiry = new OrganizationInquiry
        {
            OrganizationId = organization.Id,
            ContactName = "Контакт обращения",
            Interest = "Полировка кузова",
            CreatedByUserId = organization.OwnerUserId
        };
        seed.OrganizationInquiries.Add(inquiry);
        await seed.SaveChangesAsync();

        using var host = this.CreateHost();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);
        using var response = await client.PostAsJsonAsync(InquiryBookingRoute(organization.Id, inquiry.Id), new
        {
            version = inquiry.Version,
            newClient = new { name = "Клиент для отката", phone = "+7 999 123-45-67" },
            detailing = new
            {
                registrationPlate = "А321ВС54",
                plannedDate = s_bookingDate,
                services = Array.Empty<object>()
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var verify = fixture.CreateContext();
        Assert.Equal(0, await verify.OrganizationClients.CountAsync(item => item.OrganizationId == organization.Id));
        Assert.Equal(0, await verify.Maintenance.CountAsync(item => item.Vehicle.OrganizationId == organization.Id));
        Assert.Equal(OrganizationInquiryStageCodes.New,
            (await verify.OrganizationInquiries.AsNoTracking().SingleAsync(item => item.Id == inquiry.Id)).StageCode);
    }

    [Fact]
    public async Task InquiryBookingRequiresChoosingAnExistingClientWhenNewClientMatchesOne()
    {
        await using var seed = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seed);
        using var host = this.CreateHost();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);
        using var existingBooking = await client.PostAsJsonAsync(DetailingRoute(organization.Id), new
        {
            registrationPlate = "А789ВС54",
            clientName = "Существующий клиент",
            clientPhone = "+7 900 222-33-44",
            plannedDate = s_bookingDate,
            services = new[] { new { name = "Полировка", quantity = 1 } }
        });
        Assert.Equal(HttpStatusCode.OK, existingBooking.StatusCode);

        var inquiry = new OrganizationInquiry
        {
            OrganizationId = organization.Id,
            ContactName = "Новый контакт",
            Interest = "Полировка кузова",
            CreatedByUserId = organization.OwnerUserId
        };
        seed.OrganizationInquiries.Add(inquiry);
        await seed.SaveChangesAsync();
        using var response = await client.PostAsJsonAsync(InquiryBookingRoute(organization.Id, inquiry.Id), new
        {
            version = inquiry.Version,
            newClient = new { name = "Другое имя", phone = "+7 (900) 222-33-44" },
            detailing = new
            {
                registrationPlate = "А987ВС54",
                plannedDate = s_bookingDate.AddDays(1),
                services = new[] { new { name = "Полировка", quantity = 1 } }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var verify = fixture.CreateContext();
        Assert.Equal(1, await verify.OrganizationClients.CountAsync(item => item.OrganizationId == organization.Id));
        Assert.Equal(1, await verify.Maintenance.CountAsync(item => item.Vehicle.OrganizationId == organization.Id));
        Assert.Equal(OrganizationInquiryStageCodes.New,
            (await verify.OrganizationInquiries.AsNoTracking().SingleAsync(item => item.Id == inquiry.Id)).StageCode);
    }

    [Fact]
    public async Task InquiryBookingRejectsOccupiedVehicleSlotWithoutCompletingInquiry()
    {
        await using var seed = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seed);
        using var host = this.CreateHost();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);
        using var firstBooking = await client.PostAsJsonAsync(DetailingRoute(organization.Id), new
        {
            registrationPlate = "А456ВС54",
            clientName = "Выбранный клиент",
            clientPhone = "+7 900 555-44-33",
            plannedDate = s_bookingDate,
            plannedStartTime = new TimeSpan(9, 0, 0),
            plannedEndTime = new TimeSpan(11, 0, 0),
            location = "Пост 1",
            services = new[] { new { name = "Полировка", quantity = 1 } }
        });
        Assert.Equal(HttpStatusCode.OK, firstBooking.StatusCode);
        using var firstPayload = JsonDocument.Parse(await firstBooking.Content.ReadAsStringAsync());
        var clientId = firstPayload.RootElement.GetProperty("organizationClientId").GetGuid();

        var inquiry = new OrganizationInquiry
        {
            OrganizationId = organization.Id,
            ContactName = "Контакт обращения",
            Interest = "Дополнительная работа",
            CreatedByUserId = organization.OwnerUserId
        };
        seed.OrganizationInquiries.Add(inquiry);
        await seed.SaveChangesAsync();
        using var response = await client.PostAsJsonAsync(InquiryBookingRoute(organization.Id, inquiry.Id), new
        {
            version = inquiry.Version,
            detailing = new
            {
                registrationPlate = "А456ВС54",
                organizationClientId = clientId,
                plannedDate = s_bookingDate,
                plannedStartTime = new TimeSpan(10, 0, 0),
                plannedEndTime = new TimeSpan(12, 0, 0),
                location = "Пост 1",
                services = new[] { new { name = "Дополнительная работа", quantity = 1 } }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var verify = fixture.CreateContext();
        Assert.Equal(1, await verify.Maintenance.CountAsync(item => item.Vehicle.OrganizationId == organization.Id));
        Assert.Equal(OrganizationInquiryStageCodes.New,
            (await verify.OrganizationInquiries.AsNoTracking().SingleAsync(item => item.Id == inquiry.Id)).StageCode);
    }

    [Fact]
    public async Task DetailingQuickBookingRollsBackClientVehicleAndHistoryWhenMaintenanceValidationFails()
    {
        await using var seed = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seed);
        using var host = this.CreateHost();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);

        using var response = await client.PostAsJsonAsync(DetailingRoute(organization.Id), new
        {
            registrationPlate = "О777ОО54",
            clientName = "Откатываемый клиент",
            clientPhone = "+7 900 777-77-77",
            manualBrandName = "Лада",
            manualModelName = "Гранта",
            plannedDate = s_bookingDate,
            plannedStartTime = TimeSpan.FromHours(15),
            plannedEndTime = TimeSpan.FromHours(14),
            services = new[] { new { name = "Мойка", quantity = 1 } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("Окончание записи", error.RootElement.GetProperty("message").GetString());

        await using var verify = fixture.CreateContext();
        Assert.Equal(0, await verify.OrganizationClients.AsNoTracking().CountAsync(value => value.OrganizationId == organization.Id));
        Assert.Equal(0, await verify.Vehicles.AsNoTracking().CountAsync(value => value.OrganizationId == organization.Id));
        Assert.Equal(0, await verify.Maintenance.AsNoTracking().CountAsync(value => value.Vehicle.OrganizationId == organization.Id));
        Assert.Equal(0, await verify.OrganizationClientHistoryEvents.AsNoTracking().CountAsync(value => value.OrganizationId == organization.Id));
        Assert.Equal(0, await verify.VehicleHistoryEvents.AsNoTracking().CountAsync(value => value.OrganizationId == organization.Id));
        Assert.Equal(0, await verify.MaintenanceHistoryEvents.AsNoTracking().CountAsync(value => value.OrganizationId == organization.Id));
        Assert.Equal(0, await verify.OrganizationAuditEvents.AsNoTracking().CountAsync(value => value.OrganizationId == organization.Id));
    }

    [Theory]
    [InlineData("simple", false, MaintenanceStatus.Completed)]
    [InlineData("simple", true, MaintenanceStatus.Planned)]
    [InlineData("staged", false, MaintenanceStatus.Planned)]
    [InlineData("staged", true, MaintenanceStatus.Planned)]
    public async Task AutoServiceQuickBookingUsesSharedWorkflowStatus(
        string workflowMode,
        bool isPlannedBooking,
        MaintenanceStatus expectedStatus)
    {
        await using var seed = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seed);
        organization.PrimaryScenarioCode = "service_station";
        organization.MaintenanceWorkflowMode = workflowMode;
        await seed.SaveChangesAsync();
        using var host = this.CreateHost();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);

        using var response = await client.PostAsJsonAsync(AutoServiceRoute(organization.Id), new
        {
            registrationPlate = "М456ММ54",
            clientName = "Клиент СТО",
            plannedDate = s_bookingDate,
            isPlannedBooking,
            services = new[] { new { name = "Диагностика", quantity = 1 } }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var maintenanceId = payload.RootElement.GetProperty("maintenanceId").GetGuid();
        await using var verify = fixture.CreateContext();
        var maintenance = await verify.Maintenance.AsNoTracking().Include(value => value.Vehicle)
            .SingleAsync(value => value.Id == maintenanceId);
        Assert.Equal(organization.Id, maintenance.Vehicle.OrganizationId);
        Assert.Equal(expectedStatus, maintenance.Status);
        Assert.Equal(s_bookingDate, maintenance.PlannedDate);
        Assert.Equal(expectedStatus == MaintenanceStatus.Completed ? s_bookingDate : null, maintenance.CompletedDate);
    }

    [Theory]
    [InlineData("simple", false, MaintenanceStatus.Completed)]
    [InlineData("simple", true, MaintenanceStatus.Planned)]
    [InlineData("staged", false, MaintenanceStatus.Planned)]
    [InlineData("staged", true, MaintenanceStatus.Planned)]
    public async Task DetailingQuickBookingUsesSharedWorkflowStatus(
        string workflowMode,
        bool isPlannedBooking,
        MaintenanceStatus expectedStatus)
    {
        await using var seed = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seed);
        organization.PrimaryScenarioCode = "detailing";
        organization.MaintenanceWorkflowMode = workflowMode;
        await seed.SaveChangesAsync();
        using var host = this.CreateHost();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);

        using var response = await client.PostAsJsonAsync(DetailingRoute(organization.Id), new
        {
            registrationPlate = "А987АА54",
            clientName = "Клиент детейлинга",
            plannedDate = s_bookingDate,
            isPlannedBooking,
            services = new[] { new { name = "Полировка", quantity = 1 } }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var maintenanceId = payload.RootElement.GetProperty("maintenanceId").GetGuid();
        await using var verify = fixture.CreateContext();
        var maintenance = await verify.Maintenance.AsNoTracking().SingleAsync(value => value.Id == maintenanceId);
        Assert.Equal(expectedStatus, maintenance.Status);
        Assert.Equal(expectedStatus == MaintenanceStatus.Completed ? s_bookingDate : null, maintenance.CompletedDate);
    }

    [Fact]
    public async Task QuickBookingWithoutPlannedFlagRemainsPlannedInSimpleWorkspaces()
    {
        await using var seed = fixture.CreateContext();
        var detailingOrganization = await fixture.CreateOrganizationAsync(seed, "Детейлинг с устаревшей формой");
        detailingOrganization.PrimaryScenarioCode = "detailing";
        detailingOrganization.MaintenanceWorkflowMode = "simple";
        var autoServiceOrganization = await fixture.CreateOrganizationAsync(seed, "СТО с устаревшей формой");
        autoServiceOrganization.PrimaryScenarioCode = "service_station";
        autoServiceOrganization.MaintenanceWorkflowMode = "simple";
        await seed.SaveChangesAsync();
        using var host = this.CreateHost();
        using var detailingClient = host.GetTestClient();
        detailingClient.DefaultRequestHeaders.Add("X-Test-User", detailingOrganization.OwnerUserId);
        using var autoServiceClient = host.GetTestClient();
        autoServiceClient.DefaultRequestHeaders.Add("X-Test-User", autoServiceOrganization.OwnerUserId);

        using var detailingResponse = await detailingClient.PostAsJsonAsync(DetailingRoute(detailingOrganization.Id), new
        {
            registrationPlate = "А111АА54",
            plannedDate = s_bookingDate,
            services = new[] { new { name = "Полировка", quantity = 1 } }
        });
        using var autoServiceResponse = await autoServiceClient.PostAsJsonAsync(AutoServiceRoute(autoServiceOrganization.Id), new
        {
            registrationPlate = "А222АА54",
            plannedDate = s_bookingDate,
            services = new[] { new { name = "Диагностика", quantity = 1 } }
        });

        Assert.Equal(HttpStatusCode.OK, detailingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, autoServiceResponse.StatusCode);
        using var detailingPayload = JsonDocument.Parse(await detailingResponse.Content.ReadAsStringAsync());
        using var autoServicePayload = JsonDocument.Parse(await autoServiceResponse.Content.ReadAsStringAsync());
        var maintenanceIds = new[]
        {
            detailingPayload.RootElement.GetProperty("maintenanceId").GetGuid(),
            autoServicePayload.RootElement.GetProperty("maintenanceId").GetGuid()
        };
        await using var verify = fixture.CreateContext();
        var maintenances = await verify.Maintenance.AsNoTracking()
            .Where(value => maintenanceIds.Contains(value.Id))
            .ToListAsync();

        Assert.Equal(2, maintenances.Count);
        Assert.All(maintenances, maintenance => Assert.Equal(MaintenanceStatus.Planned, maintenance.Status));
    }

    [Fact]
    public async Task AutoServiceQuickBookingRejectsForeignVehicleAndClientBeforeWriting()
    {
        await using var seed = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seed);
        organization.PrimaryScenarioCode = "service_station";
        await seed.SaveChangesAsync();
        var foreignOrganization = await fixture.CreateOrganizationAsync(seed, "Чужая организация");
        foreignOrganization.PrimaryScenarioCode = "service_station";
        await seed.SaveChangesAsync();

        using var host = this.CreateHost();
        using var foreignClient = host.GetTestClient();
        foreignClient.DefaultRequestHeaders.Add("X-Test-User", foreignOrganization.OwnerUserId);
        using var foreignCreated = await foreignClient.PostAsJsonAsync(AutoServiceRoute(foreignOrganization.Id), new
        {
            registrationPlate = "Е111КК54",
            clientName = "Чужой клиент",
            plannedDate = s_bookingDate,
            services = new[] { new { name = "ТО", quantity = 1 } }
        });
        Assert.Equal(HttpStatusCode.OK, foreignCreated.StatusCode);
        using var foreignPayload = JsonDocument.Parse(await foreignCreated.Content.ReadAsStringAsync());
        var foreignVehicleId = foreignPayload.RootElement.GetProperty("vehicleId").GetGuid();
        var foreignClientId = foreignPayload.RootElement.GetProperty("organizationClientId").GetGuid();

        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);
        using var foreignVehicleResponse = await client.PostAsJsonAsync(AutoServiceRoute(organization.Id), new
        {
            vehicleId = foreignVehicleId,
            plannedDate = s_bookingDate,
            services = new[] { new { name = "Проверка", quantity = 1 } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, foreignVehicleResponse.StatusCode);

        using var foreignClientResponse = await client.PostAsJsonAsync(AutoServiceRoute(organization.Id), new
        {
            registrationPlate = "Т222ТТ54",
            organizationClientId = foreignClientId,
            plannedDate = s_bookingDate,
            services = new[] { new { name = "Проверка", quantity = 1 } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, foreignClientResponse.StatusCode);

        await using var verify = fixture.CreateContext();
        Assert.Equal(0, await verify.Vehicles.AsNoTracking().CountAsync(value => value.OrganizationId == organization.Id));
        Assert.Equal(0, await verify.OrganizationClients.AsNoTracking().CountAsync(value => value.OrganizationId == organization.Id));
        Assert.Equal(0, await verify.Maintenance.AsNoTracking().CountAsync(value => value.Vehicle.OrganizationId == organization.Id));
        Assert.Equal(0, await verify.OrganizationAuditEvents.AsNoTracking().CountAsync(value => value.OrganizationId == organization.Id));
        Assert.Equal(0, await verify.OrganizationClientHistoryEvents.AsNoTracking().CountAsync(value => value.OrganizationId == organization.Id));
        Assert.Equal(0, await verify.VehicleHistoryEvents.AsNoTracking().CountAsync(value => value.OrganizationId == organization.Id));
    }

    [Theory]
    [InlineData("kind", "Выбранный автомобиль не относится к указанному клиенту или типу техники.")]
    [InlineData("client", "Выбранный автомобиль не относится к указанному клиенту или типу техники.")]
    [InlineData("plate", "Измените госномер в карточке автомобиля перед созданием записи.")]
    public async Task AutoServiceSelectedVehicleMismatchPreservesErrorAndDoesNotCreateOrder(string mismatch, string expectedMessage)
    {
        await using var seed = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seed);
        organization.PrimaryScenarioCode = "service_station";
        await seed.SaveChangesAsync();
        using var host = this.CreateHost();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);
        using var created = await client.PostAsJsonAsync(AutoServiceRoute(organization.Id), new
        {
            registrationPlate = "А123ВС54",
            clientName = "Владелец автомобиля",
            plannedDate = s_bookingDate,
            services = new[] { new { name = "ТО", quantity = 1 } }
        });

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var payload = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var vehicleId = payload.RootElement.GetProperty("vehicleId").GetGuid();
        var clientId = payload.RootElement.GetProperty("organizationClientId").GetGuid();
        using var rejected = await client.PostAsJsonAsync(AutoServiceRoute(organization.Id), new
        {
            vehicleId,
            registrationPlate = mismatch == "plate" ? "В999ВВ54" : "A 123 BC 54",
            organizationClientId = mismatch == "client" ? Guid.NewGuid() : clientId,
            kind = mismatch == "kind" ? VehicleKind.Truck : VehicleKind.PassengerCar,
            plannedDate = s_bookingDate,
            services = new[] { new { name = "Повторное ТО", quantity = 1 } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using var error = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        Assert.Equal(expectedMessage, error.RootElement.GetProperty("message").GetString());
        await using var verify = fixture.CreateContext();
        Assert.Equal(1, await verify.Maintenance.CountAsync(item => item.Vehicle.OrganizationId == organization.Id));
        Assert.Equal(1, await verify.Vehicles.CountAsync(item => item.OrganizationId == organization.Id));
        Assert.Equal(1, await verify.OrganizationClients.CountAsync(item => item.OrganizationId == organization.Id));
    }

    [Theory]
    [InlineData("detailing")]
    [InlineData("service_station")]
    public async Task ConcurrentQuickBookingsReuseOneClient(string scenario)
    {
        await using var seed = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seed);
        organization.PrimaryScenarioCode = scenario;
        await seed.SaveChangesAsync();
        using var host = this.CreateHost(new ConcurrentClientLockInterceptor());
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);
        var route = scenario == "detailing" ? DetailingRoute(organization.Id) : AutoServiceRoute(organization.Id);

        var requests = new[] { "А111АА54", "В222ВВ54" }.Select(plate => client.PostAsJsonAsync(route, new
        {
            registrationPlate = plate,
            clientName = "Общий клиент",
            clientPhone = "+7 900 123-45-67",
            manualBrandName = "Лада",
            manualModelName = "Веста",
            plannedDate = s_bookingDate,
            services = new[] { new { name = "Полировка", quantity = 1 } }
        }));

        var responses = await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            var results = new List<JsonDocument>();
            try
            {
                foreach (var response in responses)
                {
                    results.Add(JsonDocument.Parse(await response.Content.ReadAsStringAsync()));
                }

                var clientIds = results.Select(result => result.RootElement.GetProperty("organizationClientId").GetGuid());
                Assert.Single(clientIds.Distinct());
                Assert.Single(results, result => result.RootElement.GetProperty("clientCreated").GetBoolean());
            }
            finally
            {
                results.ForEach(result => result.Dispose());
            }

            await using var verify = fixture.CreateContext();
            Assert.Equal(1, await verify.OrganizationClients.CountAsync(value => value.OrganizationId == organization.Id));
            Assert.Equal(2, await verify.Vehicles.CountAsync(value => value.OrganizationId == organization.Id));
            Assert.Equal(2, await verify.Maintenance.CountAsync(value => value.Vehicle.OrganizationId == organization.Id));
            Assert.Equal(1, await verify.OrganizationClientHistoryEvents.CountAsync(value => value.OrganizationId == organization.Id));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    private IHost CreateHost(params IInterceptor[] interceptors)
    {
        var builder = new HostBuilder()
            .ConfigureAppConfiguration(config => config.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "organization-appsettings.json")))
            .ConfigureWebHost(web => web.UseTestServer().ConfigureServices(services =>
        {
            services.AddScoped<OpenCRMContext>(_ => fixture.CreateContext(interceptors));
            services.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<OpenCRMContext>();
            services.AddOpenCRMRepositories();
            services.AddScoped<IApplicationUnitOfWork, ApplicationUnitOfWork>();
            services.AddScoped<IOrganizationSubscriptionService>(provider =>
                CreateSubscription(provider.GetRequiredService<OpenCRMContext>()));
            services.AddOpenCRMCatalog();
            services.AddOpenCRMClients();
            services.AddOpenCRMVehicles();
            services.AddOpenCRMMaintenance();
            services.AddOpenCRMOrganizations();
            services.AddScoped<IOrganizationQueryService>(provider =>
            {
                var context = provider.GetRequiredService<OpenCRMContext>();
                var query = new Mock<IOrganizationQueryService>();
                query.Setup(value => value.GetByIdAsync(It.IsAny<string>(), It.IsAny<Guid>()))
                    .Returns<string, Guid>(async (_, organizationId) =>
                    {
                        var organization = await context.Organizations.AsNoTracking()
                            .Where(value => value.Id == organizationId)
                            .Select(value => new { value.PrimaryScenarioCode, value.MaintenanceWorkflowMode })
                            .SingleAsync();
                        return new OrganizationDto
                        {
                            Id = organizationId,
                            PrimaryScenarioCode = organization.PrimaryScenarioCode,
                            MaintenanceWorkflowMode = organization.MaintenanceWorkflowMode
                        };
                    });

                return query.Object;
            });
            var clock = new Mock<OpenCRMSystemClock>();
            clock.SetupGet(value => value.UtcNow).Returns(DateTime.UtcNow);
            clock.SetupGet(value => value.UtcToday).Returns(DateTime.UtcNow.Date);
            services.AddSingleton(clock.Object);
            var featureAccess = new Mock<IFeatureAccessService>();
            featureAccess.Setup(value => value.GetIntValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(1000);
            services.AddSingleton(featureAccess.Object);
            services.AddSingleton(Mock.Of<IPersonalSubscriptionQueryService>());
            services.AddSingleton(Mock.Of<IMaintenanceSchedulerSyncService>());
            var consumption = new Mock<IMaintenanceConsumptionService>();
            consumption.Setup(value => value.SynchronizeAsync(
                    It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<MaintenanceMaterialConsumptionLine>>()))
                .ReturnsAsync(new MaintenanceConsumptionResult(0, []));
            services.AddSingleton(consumption.Object);
            services.AddSingleton(Mock.Of<OpenCRM.Services.Interfaces.FileStorages.IFileStorageService>());
            services.AddAuthentication("QuickBookingTest")
                .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("QuickBookingTest", _ => { });
            services.AddAuthorization();
            services.AddControllers().AddApplicationPart(typeof(OrganizationDetailingQuickBookingsApiController).Assembly);
        }).Configure(application =>
        {
            application.UseRouting();
            application.UseAuthentication();
            application.UseAuthorization();
            application.UseEndpoints(endpoints => endpoints.MapControllers());
        }));
        var host = builder.Build();
        host.Start();

        return host;
    }

    private static IOrganizationSubscriptionService CreateSubscription(OpenCRMContext context)
    {
        var subscription = new Mock<IOrganizationSubscriptionService>();
        subscription.Setup(value => value.HasFeatureAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        subscription.Setup(value => value.GetVehicleLimitAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<int>()))
            .ReturnsAsync(1000);
        subscription.Setup(value => value.GetFeatureSnapshotAsync(It.IsAny<string>(), It.IsAny<Guid>()))
            .ReturnsAsync(new FeatureAccessSnapshotDto
            {
                Features =
                [
                    new FeatureValueDto
                    {
                        Code = Helpers.FeatureCodes.WorkspaceClientsLimit,
                        IntValue = int.MaxValue
                    }
                ]
            });
        subscription.Setup(value => value.GetWorkspaceAccessAsync(It.IsAny<string>(), It.IsAny<Guid>()))
            .ReturnsAsync(new OrganizationWorkspaceAccessDto
            {
                IsFreePlan = false,
                HasActiveSubscription = true,
                IsReadOnly = false
            });
        subscription.Setup(value => value.EnsureWorkspaceCanReadAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns<string, Guid, string>((userId, organizationId, _) => EnsureMemberAsync(userId, organizationId));
        subscription.Setup(value => value.EnsureWorkspaceCanWriteAsync(It.IsAny<string>(), It.IsAny<Guid>()))
            .Returns<string, Guid>(EnsureMemberAsync);
        subscription.Setup(value => value.EnsureWorkspaceCanWriteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns<string, Guid, string>((userId, organizationId, _) => EnsureMemberAsync(userId, organizationId));

        return subscription.Object;

        async Task EnsureMemberAsync(string userId, Guid organizationId)
        {
            if (!await context.OrganizationMembers.AsNoTracking()
                .AnyAsync(value => value.OrganizationId == organizationId && value.UserId == userId))
            {
                throw new UnauthorizedAccessException("Нет доступа к организации.");
            }
        }
    }

    private static string DetailingRoute(Guid organizationId) => $"/api/v1/organizations/{organizationId}/detailing/bookings/quick";

    private static string AutoServiceRoute(Guid organizationId) => $"/api/v1/organizations/{organizationId}/autoservice/bookings/quick";

    private static string InquiryBookingRoute(Guid organizationId, Guid inquiryId) =>
        $"/api/v1/organizations/{organizationId}/inquiries/{inquiryId}/booking";

    private sealed class ConcurrentClientLockInterceptor : DbCommandInterceptor
    {
        private readonly ConcurrentDictionary<Guid, bool> _contexts = new();
        private readonly TaskCompletionSource _bothRequests = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!command.CommandText.Contains("FOR NO KEY UPDATE", StringComparison.Ordinal)
                || !this._contexts.TryAdd(eventData.Context!.ContextId.InstanceId, true))
            {
                return result;
            }

            // Оба запроса доходят до первой блокировки до продолжения любого из них.
            if (this._contexts.Count == 2)
            {
                this._bothRequests.TrySetResult();
            }

            await this._bothRequests.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);

            return result;
        }
    }

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var userId = this.Request.Headers["X-Test-User"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], this.Scheme.Name);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), this.Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
