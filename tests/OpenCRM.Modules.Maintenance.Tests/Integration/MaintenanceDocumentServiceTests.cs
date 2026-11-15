using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Testing;
using Npgsql;
using System.Data.Common;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class MaintenanceDocumentServiceTests(MaintenanceDatabaseFixture fixture) : IClassFixture<MaintenanceDatabaseFixture>
{
    [Fact]
    public async Task ExistingActNumberIsReturnedWithoutWritePermission()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceDocumentServiceTests.CreateWork(isOrganization: true);
        work.WorkActOrganizationId = work.Vehicle.Organization!.Id;
        work.WorkActNumber = 42;
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();

        var access = new Mock<IOrganizationSubscriptionService>(MockBehavior.Strict);
        access.Setup(service => service.EnsureWorkspaceCanReadAsync(
                work.User.Id, work.Vehicle.Organization!.Id, OrganizationPermissionCodes.MaintenanceView))
            .Returns(Task.CompletedTask);
        using var service = MaintenanceDocumentServiceTests.CreateService(context, access.Object);

        var number = await service.EnsureWorkActNumber(work.User.Id, work.Vehicle.Organization!.Id, work.Id);

        Assert.Equal(42, number);
        access.Verify(value => value.EnsureWorkspaceCanWriteAsync(
            work.User.Id, work.Vehicle.Organization!.Id, OrganizationPermissionCodes.MaintenanceEdit), Times.Never);
    }

    [Fact]
    public async Task ActNumbersAreSequentialAndScopedToEachOrganization()
    {
        await using var context = fixture.CreateContext();
        var first = MaintenanceDocumentServiceTests.CreateWork(isOrganization: true);
        var second = MaintenanceDocumentServiceTests.CreateWork(isOrganization: true);
        second.User = first.User;
        second.Vehicle.User = first.User;
        second.Vehicle.Organization = first.Vehicle.Organization;
        var otherOrganization = MaintenanceDocumentServiceTests.CreateWork(isOrganization: true);
        context.Maintenance.AddRange(first, second, otherOrganization);
        await context.SaveChangesAsync();
        using var service = MaintenanceDocumentServiceTests.CreateService(context);

        Assert.Equal(1, await service.EnsureWorkActNumber(first.User.Id, first.Vehicle.Organization!.Id, first.Id));
        Assert.Equal(2, await service.EnsureWorkActNumber(second.User.Id, first.Vehicle.Organization!.Id, second.Id));
        Assert.Equal(1, await service.EnsureWorkActNumber(
            otherOrganization.User.Id, otherOrganization.Vehicle.Organization!.Id, otherOrganization.Id));

        context.ChangeTracker.Clear();
        var savedWorks = await context.Maintenance
            .Where(value => value.Id == first.Id || value.Id == second.Id || value.Id == otherOrganization.Id)
            .ToDictionaryAsync(value => value.Id);
        Assert.Equal(1, savedWorks[first.Id].WorkActNumber);
        Assert.Equal(first.Vehicle.Organization!.Id, savedWorks[first.Id].WorkActOrganizationId);
        Assert.Equal(2, savedWorks[second.Id].WorkActNumber);
        Assert.Equal(1, savedWorks[otherOrganization.Id].WorkActNumber);
        Assert.Equal(otherOrganization.Vehicle.Organization!.Id, savedWorks[otherOrganization.Id].WorkActOrganizationId);
    }

    [Fact]
    public async Task ConcurrentlyAssignedActNumberIsReusedInsideTheTransaction()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceDocumentServiceTests.CreateWork(isOrganization: true);
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var organizationId = work.Vehicle.Organization!.Id;
        var access = new Mock<IOrganizationSubscriptionService>();
        access.Setup(value => value.EnsureWorkspaceCanWriteAsync(
                work.User.Id, organizationId, OrganizationPermissionCodes.MaintenanceEdit))
            .Returns(async () =>
            {
                await using var concurrentContext = fixture.CreateContext();
                await concurrentContext.Maintenance.Where(value => value.Id == work.Id)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(value => value.WorkActNumber, 42)
                        .SetProperty(value => value.WorkActOrganizationId, organizationId));
            });
        using var service = MaintenanceDocumentServiceTests.CreateService(context, access.Object);

        var number = await service.EnsureWorkActNumber(work.User.Id, organizationId, work.Id);

        Assert.Equal(42, number);
        Assert.Equal(42, work.WorkActNumber);
        Assert.Equal(42, await context.Maintenance.Where(value => value.Id == work.Id)
            .Select(value => value.WorkActNumber).SingleAsync());
    }

    [Fact]
    public async Task DocumentWritesRejectMissingOrderAndAnotherPersonalOwner()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceDocumentServiceTests.CreateWork(isOrganization: false);
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        using var service = MaintenanceDocumentServiceTests.CreateService(context);

        await Assert.ThrowsAsync<Exception>(() => service.TrackDocumentGenerated(
            work.User.Id, null, Guid.NewGuid(), "Акт"));
        await Assert.ThrowsAsync<Exception>(() => service.AddDocument(
            work.User.Id, null, Guid.NewGuid(), MaintenanceDocumentServiceTests.CreateDocumentRequest("missing.pdf")));
        await Assert.ThrowsAsync<Exception>(() => service.AddDocument(
            "another-owner", null, work.Id, MaintenanceDocumentServiceTests.CreateDocumentRequest("foreign.pdf")));

        Assert.False(await context.MaintenanceDocuments.AnyAsync(value => value.MaintenanceId == work.Id));
        Assert.False(await context.MaintenanceHistoryEvents.AnyAsync(value => value.MaintenanceId == work.Id));
    }

    [Fact]
    public async Task ConcurrentActNumbersAreStableForOneOrderAndUniqueAcrossOrders()
    {
        await using var context = fixture.CreateContext();
        var first = MaintenanceDocumentServiceTests.CreateWork(isOrganization: true);
        var works = Enumerable.Range(0, 3).Select(_ =>
        {
            var work = MaintenanceDocumentServiceTests.CreateWork(isOrganization: true);
            work.User = first.User;
            work.Vehicle.User = first.User;
            work.Vehicle.Organization = first.Vehicle.Organization;

            return work;
        }).Prepend(first).ToArray();
        context.Maintenance.AddRange(works);
        await context.SaveChangesAsync();
        var organizationId = first.Vehicle.Organization!.Id;
        var requests = works.Concat([first, first, first]).Select(async work =>
        {
            await using var exportContext = fixture.CreateContext();
            using var service = MaintenanceDocumentServiceTests.CreateService(exportContext);
            var number = await service.EnsureWorkActNumber(first.User.Id, organizationId, work.Id);

            return (work.Id, Number: number);
        });

        var results = await Task.WhenAll(requests);

        Assert.Single(results.Where(value => value.Id == first.Id).Select(value => value.Number).Distinct());
        Assert.Equal(new[] { 1, 2, 3, 4 }, results.Select(value => value.Number).Distinct().Order().ToArray());
        context.ChangeTracker.Clear();
        var saved = await context.Maintenance.Where(value => value.WorkActOrganizationId == organizationId).ToListAsync();
        Assert.Equal(4, saved.Count);
        Assert.Equal(4, saved.Select(value => value.WorkActNumber).Distinct().Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnscopedOrPreviousActNumberDoesNotReserveANumberInTheCurrentOrganization(bool hasPreviousOrganization)
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceDocumentServiceTests.CreateWork(isOrganization: true);
        var previousOrganization = MaintenanceDocumentServiceTests.CreateWork(isOrganization: true).Vehicle.Organization!;
        context.Organizations.Add(previousOrganization);
        work.WorkActOrganizationId = hasPreviousOrganization ? previousOrganization.Id : null;
        work.WorkActNumber = 42;
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        using var service = MaintenanceDocumentServiceTests.CreateService(context);
        var organizationId = work.Vehicle.Organization!.Id;

        var number = await service.EnsureWorkActNumber(work.User.Id, organizationId, work.Id);

        Assert.Equal(1, number);
        Assert.Equal(organizationId, work.WorkActOrganizationId);
        Assert.Equal(1, work.WorkActNumber);
    }

    [Fact]
    public async Task ActNumberRetriesTransientSaveFailureWithoutLeavingPartialNumber()
    {
        var interceptor = new FailFirstSaveInterceptor();
        await using var context = fixture.CreateContext(interceptor);
        var work = MaintenanceDocumentServiceTests.CreateWork(isOrganization: true);
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        interceptor.FailNextSave();
        using var service = MaintenanceDocumentServiceTests.CreateService(context);
        var organizationId = work.Vehicle.Organization!.Id;

        var number = await service.EnsureWorkActNumber(work.User.Id, organizationId, work.Id);

        Assert.Equal(1, number);
        Assert.Equal(1, interceptor.FailureCount);
        context.ChangeTracker.Clear();
        var savedWork = await context.Maintenance.SingleAsync(value => value.Id == work.Id);
        Assert.Equal(1, savedWork.WorkActNumber);
        Assert.Equal(organizationId, savedWork.WorkActOrganizationId);
    }

    [Fact]
    public async Task ActNumberRejectsIncompleteMissingForeignAndDeletedMaintenance()
    {
        await using var context = fixture.CreateContext();
        var incomplete = MaintenanceDocumentServiceTests.CreateWork(isOrganization: true, MaintenanceStatus.Planned);
        var deleted = MaintenanceDocumentServiceTests.CreateWork(isOrganization: true);
        deleted.IsDeleted = true;
        var foreign = MaintenanceDocumentServiceTests.CreateWork(isOrganization: true);
        context.Maintenance.AddRange(incomplete, deleted, foreign);
        await context.SaveChangesAsync();
        using var service = MaintenanceDocumentServiceTests.CreateService(context);
        var organizationId = incomplete.Vehicle.Organization!.Id;

        await Assert.ThrowsAsync<Exception>(() => service.EnsureWorkActNumber(
            incomplete.User.Id, organizationId, incomplete.Id));
        await Assert.ThrowsAsync<Exception>(() => service.EnsureWorkActNumber(
            incomplete.User.Id, organizationId, Guid.NewGuid()));
        await Assert.ThrowsAsync<Exception>(() => service.EnsureWorkActNumber(
            incomplete.User.Id, organizationId, foreign.Id));
        await Assert.ThrowsAsync<Exception>(() => service.EnsureWorkActNumber(
            incomplete.User.Id, organizationId, deleted.Id));

        context.ChangeTracker.Clear();
        Assert.Null(await context.Maintenance.Where(value => value.Id == incomplete.Id)
            .Select(value => value.WorkActNumber).SingleAsync());
        Assert.Null(await context.Maintenance.Where(value => value.Id == foreign.Id)
            .Select(value => value.WorkActNumber).SingleAsync());
    }

    [Fact]
    public async Task AddDocumentNormalizesMetadataTruncatesFieldsAndVersionsByPhotoSelection()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceDocumentServiceTests.CreateWork(isOrganization: true);
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var normalizedKind = new string('k', 64);
        var normalizedFormat = new string('P', 16);
        var older = MaintenanceDocumentServiceTests.CreateDocument(
            work, normalizedKind, normalizedFormat, includePhotos: false, version: 1, createdAt: DateTime.UtcNow.AddMinutes(-3));
        var deletedHighVersion = MaintenanceDocumentServiceTests.CreateDocument(
            work, normalizedKind, normalizedFormat, includePhotos: false, version: 50, createdAt: DateTime.UtcNow.AddMinutes(-2));
        deletedHighVersion.IsDeleted = true;
        var photoVersion = MaintenanceDocumentServiceTests.CreateDocument(
            work, normalizedKind, normalizedFormat, includePhotos: true, version: 1, createdAt: DateTime.UtcNow.AddMinutes(-1));
        context.MaintenanceDocuments.AddRange(older, deletedHighVersion, photoVersion);
        await context.SaveChangesAsync();
        using var service = MaintenanceDocumentServiceTests.CreateService(context);
        var organizationId = work.Vehicle.Organization!.Id;
        var dto = new CreateMaintenanceDocumentDto
        {
            Kind = new string('K', 70),
            Format = new string('p', 20),
            IncludePhotos = false,
            Title = $" {new string('я', 260)} ",
            FileName = $" {new string('f', 270)} ",
            StoragePath = $" /{new string('a', 510)} ",
            ContentType = $" {new string('c', 130)} ",
            Size = 123456
        };

        var document = await service.AddDocument(work.User.Id, organizationId, work.Id, dto);
        var photoDocument = await service.AddDocument(work.User.Id, organizationId, work.Id, new CreateMaintenanceDocumentDto
        {
            Kind = $" {new string('K', 70)} ",
            Format = $" {new string('p', 20)} ",
            IncludePhotos = true,
            Title = "С актом фото",
            FileName = "акт.pdf",
            StoragePath = "/docs/act-photo.pdf",
            ContentType = "application/pdf",
            Size = 250
        });

        Assert.Equal(new string('k', 64), document.Kind);
        Assert.Equal(new string('P', 16), document.Format);
        Assert.False(document.IncludePhotos);
        Assert.Equal(2, document.Version);
        Assert.Equal(new string('я', 250), document.Title);
        Assert.Equal(new string('f', 260), document.FileName);
        Assert.Equal(new string('c', 120), document.ContentType);
        Assert.Equal(123456L, document.Size);
        Assert.Equal(work.User.Email, document.CreatedByDisplayName);
        Assert.Equal(2, photoDocument.Version);

        context.ChangeTracker.Clear();
        var saved = await context.MaintenanceDocuments.SingleAsync(value => value.Id == document.Id);
        Assert.Equal($"/{new string('a', 499)}", saved.StoragePath);
        Assert.Equal(work.Vehicle.Organization!.Id, saved.OrganizationId);
        Assert.Equal(work.User.Id, saved.CreatedByUserId);
    }

    [Theory]
    [InlineData(" ", "pdf", "/docs/file.pdf")]
    [InlineData("work_order", " ", "/docs/file.pdf")]
    [InlineData("work_order", "pdf", " ")]
    [InlineData(null, "pdf", "/docs/file.pdf")]
    [InlineData("work_order", null, "/docs/file.pdf")]
    [InlineData("work_order", "pdf", null)]
    public async Task AddDocumentRejectsEmptyKindFormatAndStoragePath(string? kind, string? format, string? storagePath)
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceDocumentServiceTests.CreateWork(isOrganization: true);
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        using var service = MaintenanceDocumentServiceTests.CreateService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddDocument(
            work.User.Id,
            work.Vehicle.Organization!.Id,
            work.Id,
            new CreateMaintenanceDocumentDto
            {
                Kind = kind!,
                Format = format!,
                IncludePhotos = false,
                Title = "Документ",
                FileName = "document.pdf",
                StoragePath = storagePath!,
                ContentType = "application/pdf",
                Size = 10
            }));

        Assert.False(await context.MaintenanceDocuments.AnyAsync(value => value.MaintenanceId == work.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DocumentListContainsOnlyActiveSignedDocumentsInNewestFirstOrder(bool isOrganization)
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceDocumentServiceTests.CreateWork(isOrganization);
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var oldest = MaintenanceDocumentServiceTests.CreateDocument(
            work, "signed_document", "PDF", false, 1, new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc));
        var newest = MaintenanceDocumentServiceTests.CreateDocument(
            work, "signed_document", "PDF", false, 2, new DateTime(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc));
        var generated = MaintenanceDocumentServiceTests.CreateDocument(
            work, "maintenance_report", "PDF", false, 1, new DateTime(2026, 9, 4, 10, 0, 0, DateTimeKind.Utc));
        var deleted = MaintenanceDocumentServiceTests.CreateDocument(
            work, "signed_document", "PDF", false, 3, new DateTime(2026, 9, 5, 10, 0, 0, DateTimeKind.Utc));
        deleted.IsDeleted = true;
        context.MaintenanceDocuments.AddRange(oldest, newest, generated, deleted);
        await context.SaveChangesAsync();
        using var service = MaintenanceDocumentServiceTests.CreateService(context);
        var organizationId = isOrganization ? work.Vehicle.Organization!.Id : (Guid?)null;

        var documents = await service.GetDocuments(work.User.Id, organizationId, work.Id);

        Assert.Collection(
            documents,
            value => Assert.Equal(newest.Id, value.Id),
            value => Assert.Equal(oldest.Id, value.Id));
    }

    [Fact]
    public async Task AddListAndDownloadRespectPersonalAndOrganizationScopes()
    {
        await using var context = fixture.CreateContext();
        var personalWork = MaintenanceDocumentServiceTests.CreateWork(isOrganization: false);
        var organizationWork = MaintenanceDocumentServiceTests.CreateWork(isOrganization: true);
        context.Maintenance.AddRange(personalWork, organizationWork);
        await context.SaveChangesAsync();
        using var service = MaintenanceDocumentServiceTests.CreateService(context);
        var personalDocument = await service.AddDocument(
            personalWork.User.Id, null, personalWork.Id, MaintenanceDocumentServiceTests.CreateDocumentRequest("personal.pdf"));
        var organizationDocument = await service.AddDocument(
            organizationWork.User.Id,
            organizationWork.Vehicle.Organization!.Id,
            organizationWork.Id,
            MaintenanceDocumentServiceTests.CreateDocumentRequest("organization.pdf"));

        Assert.Single(await service.GetDocuments(personalWork.User.Id, null, personalWork.Id));
        Assert.Single(await service.GetDocuments(
            organizationWork.User.Id, organizationWork.Vehicle.Organization!.Id, organizationWork.Id));
        Assert.Equal(personalDocument.Id, (await service.GetDocumentForDownload(personalWork.User.Id, null, personalDocument.Id)).Id);
        Assert.Equal(
            organizationDocument.Id,
            (await service.GetDocumentForDownload(
                organizationWork.User.Id, organizationWork.Vehicle.Organization!.Id, organizationDocument.Id)).Id);

        await Assert.ThrowsAsync<Exception>(() => service.GetDocuments(
            personalWork.User.Id, organizationWork.Vehicle.Organization!.Id, personalWork.Id));
        await Assert.ThrowsAsync<Exception>(() => service.GetDocuments(
            organizationWork.User.Id, null, organizationWork.Id));
        await Assert.ThrowsAsync<Exception>(() => service.GetDocumentForDownload(
            personalWork.User.Id, organizationWork.Vehicle.Organization!.Id, personalDocument.Id));
        await Assert.ThrowsAsync<Exception>(() => service.GetDocumentForDownload(
            organizationWork.User.Id, null, organizationDocument.Id));
        await Assert.ThrowsAsync<Exception>(() => service.AddDocument(
            personalWork.User.Id,
            organizationWork.Vehicle.Organization!.Id,
            personalWork.Id,
            MaintenanceDocumentServiceTests.CreateDocumentRequest("wrong-organization.pdf")));
        await Assert.ThrowsAsync<Exception>(() => service.AddDocument(
            organizationWork.User.Id,
            null,
            organizationWork.Id,
            MaintenanceDocumentServiceTests.CreateDocumentRequest("wrong-personal-scope.pdf")));

        context.ChangeTracker.Clear();
        Assert.Null(await context.MaintenanceDocuments.Where(value => value.Id == personalDocument.Id)
            .Select(value => value.OrganizationId).SingleAsync());
        Assert.Equal(organizationWork.Vehicle.Organization!.Id, await context.MaintenanceDocuments
            .Where(value => value.Id == organizationDocument.Id)
            .Select(value => value.OrganizationId).SingleAsync());
    }

    [Fact]
    public async Task DownloadRejectsDeletedDocumentDeletedMaintenanceAndMissingDocument()
    {
        await using var context = fixture.CreateContext();
        var documentWork = MaintenanceDocumentServiceTests.CreateWork(isOrganization: true);
        var deletedWork = MaintenanceDocumentServiceTests.CreateWork(isOrganization: true);
        context.Maintenance.AddRange(documentWork, deletedWork);
        await context.SaveChangesAsync();
        using var service = MaintenanceDocumentServiceTests.CreateService(context);
        var organizationId = documentWork.Vehicle.Organization!.Id;
        var activeDocument = await service.AddDocument(
            documentWork.User.Id, organizationId, documentWork.Id,
            MaintenanceDocumentServiceTests.CreateDocumentRequest("active.pdf"));
        var deletedDocument = await service.AddDocument(
            documentWork.User.Id, organizationId, documentWork.Id,
            MaintenanceDocumentServiceTests.CreateDocumentRequest("deleted.pdf"));
        var deletedMaintenanceDocument = await service.AddDocument(
            deletedWork.User.Id, deletedWork.Vehicle.Organization!.Id, deletedWork.Id,
            MaintenanceDocumentServiceTests.CreateDocumentRequest("deleted-maintenance.pdf"));

        var storedDeleted = await context.MaintenanceDocuments.SingleAsync(value => value.Id == deletedDocument.Id);
        storedDeleted.IsDeleted = true;
        var storedWork = await context.Maintenance.SingleAsync(value => value.Id == deletedWork.Id);
        storedWork.IsDeleted = true;
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<Exception>(() => service.GetDocumentForDownload(
            documentWork.User.Id, organizationId, deletedDocument.Id));
        await Assert.ThrowsAsync<Exception>(() => service.GetDocumentForDownload(
            deletedWork.User.Id, deletedWork.Vehicle.Organization!.Id, deletedMaintenanceDocument.Id));
        await Assert.ThrowsAsync<Exception>(() => service.GetDocumentForDownload(
            documentWork.User.Id, organizationId, Guid.NewGuid()));
        var download = await service.GetDocumentForDownload(documentWork.User.Id, organizationId, activeDocument.Id);
        Assert.Equal(activeDocument.FileName, download.FileName);
        Assert.Equal(activeDocument.ContentType, download.ContentType);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TrackDocumentGeneratedWritesHistoryWithPersonalOrOrganizationScope(bool isOrganization)
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceDocumentServiceTests.CreateWork(isOrganization);
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        using var service = MaintenanceDocumentServiceTests.CreateService(context);
        var organizationId = isOrganization ? work.Vehicle.Organization!.Id : (Guid?)null;

        await service.TrackDocumentGenerated(work.User.Id, organizationId, work.Id, "Акт сформирован", "PDF, с фотографиями");

        var history = await context.MaintenanceHistoryEvents.SingleAsync(value => value.MaintenanceId == work.Id);
        Assert.Equal(MaintenanceHistoryEventCodes.EventDocumentGenerated, history.EventType);
        Assert.Equal("Акт сформирован", history.Title);
        Assert.Equal("PDF, с фотографиями", history.Description);
        Assert.Equal(work.User.Id, history.UserId);
        Assert.Equal(organizationId, history.OrganizationId);
        Assert.Equal(work.Vehicle.Id, history.VehicleId);
        Assert.Equal(work.User.Email, history.ActorDisplayName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task DocumentMetadataPreservesEmptyFieldsAndUsesDefaultsForNull(string? value)
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceDocumentServiceTests.CreateWork(isOrganization: false);
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        using var service = MaintenanceDocumentServiceTests.CreateService(context);
        var request = MaintenanceDocumentServiceTests.CreateDocumentRequest("file.pdf");
        request.Title = value!;
        request.FileName = value!;
        request.ContentType = value!;

        var document = await service.AddDocument(work.User.Id, null, work.Id, request);

        Assert.Equal(value == null ? "Документ обслуживания" : "", document.Title);
        Assert.Equal(value == null ? "maintenance-document" : "", document.FileName);
        Assert.Equal(value == null ? "application/octet-stream" : "", document.ContentType);
    }

    [Fact]
    public async Task RepeatedActNumberSaveConflictsPropagateWithoutPersistingANumber()
    {
        var interceptor = new FailFirstSaveInterceptor();
        await using var context = fixture.CreateContext(interceptor);
        var work = MaintenanceDocumentServiceTests.CreateWork(isOrganization: true);
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        interceptor.FailNextSave(3);
        using var service = MaintenanceDocumentServiceTests.CreateService(context);

        await Assert.ThrowsAsync<DbUpdateException>(() => service.EnsureWorkActNumber(
            work.User.Id, work.Vehicle.Organization!.Id, work.Id));

        Assert.Equal(3, interceptor.FailureCount);
        context.ChangeTracker.Clear();
        Assert.Null((await context.Maintenance.SingleAsync(value => value.Id == work.Id)).WorkActNumber);
    }

    [Fact]
    public async Task ActNumberRetriesASerializationFailureDuringCommit()
    {
        var interceptor = new FailActCommitInterceptor();
        await using var context = fixture.CreateContext(interceptor);
        var work = MaintenanceDocumentServiceTests.CreateWork(isOrganization: true);
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        interceptor.FailNextCommit = true;
        using var service = MaintenanceDocumentServiceTests.CreateService(context);
        var organizationId = work.Vehicle.Organization!.Id;

        var number = await service.EnsureWorkActNumber(work.User.Id, organizationId, work.Id);

        Assert.Equal(1, number);
        Assert.Equal(1, interceptor.FailureCount);
        context.ChangeTracker.Clear();
        Assert.Equal(1, (await context.Maintenance.SingleAsync(value => value.Id == work.Id)).WorkActNumber);
    }

    private static MaintenanceTestScope CreateService(
        OpenCRMContext context,
        IOrganizationSubscriptionService? access = null)
    {
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc));

        return new MaintenanceTestScope(
            new MaintenanceRepository(context),
            Mock.Of<IVehicleCounterCommandService>(),
            clock.Object,
            context,
            access ?? Mock.Of<IOrganizationSubscriptionService>(),
            Mock.Of<IMaintenanceQueryService>(),
            Mock.Of<IMaintenanceSlotQueryService>(),
            Mock.Of<IMaintenanceConsumptionService>());
    }

    private static MaintenanceEntity CreateWork(bool isOrganization, MaintenanceStatus status = MaintenanceStatus.Completed)
    {
        var user = new ApplicationUser
        {
            Email = $"document-{Guid.NewGuid():N}@example.test",
            UserName = $"document-{Guid.NewGuid():N}"
        };
        var vehicle = new Vehicle
        {
            User = user,
            RegistrationPlate = "А123АА154"
        };
        if (isOrganization)
        {
            vehicle.Organization = new Organization
            {
                Name = "Обслуживание документов",
                OwnerUser = user,
                Members = [new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Owner }]
            };
        }

        var date = new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);

        return new MaintenanceEntity
        {
            Vehicle = vehicle,
            User = user,
            Status = status,
            Type = MaintenanceType.Maintenance,
            Date = date,
            CompletedDate = status == MaintenanceStatus.Completed ? date : null,
            DateTimeCreation = date.AddDays(-1),
            Details = [new() { Name = "Полировка", Price = 100 }]
        };
    }

    private static MaintenanceDocument CreateDocument(
        MaintenanceEntity work,
        string kind,
        string format,
        bool includePhotos,
        int version,
        DateTime createdAt)
    {
        return new MaintenanceDocument
        {
            Maintenance = work,
            VehicleId = work.VehicleId,
            OrganizationId = work.Vehicle.OrganizationId,
            User = work.User,
            UserId = work.User.Id,
            CreatedByUserId = work.User.Id,
            CreatedByDisplayName = work.User.Email,
            Kind = kind,
            Format = format,
            IncludePhotos = includePhotos,
            Title = kind,
            FileName = $"{kind}.{format.ToLowerInvariant()}",
            StoragePath = $"/documents/{Guid.NewGuid():N}",
            ContentType = "application/pdf",
            Size = 10,
            Version = version,
            CreatedAt = createdAt
        };
    }

    private static CreateMaintenanceDocumentDto CreateDocumentRequest(string fileName)
    {
        return new CreateMaintenanceDocumentDto
        {
            Kind = "signed_document",
            Format = "pdf",
            IncludePhotos = false,
            Title = "Подписанный документ",
            FileName = fileName,
            StoragePath = $"/documents/{fileName}",
            ContentType = "application/pdf",
            Size = 100
        };
    }

    private sealed class FailActCommitInterceptor : DbTransactionInterceptor
    {
        public bool FailNextCommit { get; set; }

        public int FailureCount { get; private set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (!this.FailNextCommit)
            {
                return ValueTask.FromResult(result);
            }

            this.FailNextCommit = false;
            this.FailureCount++;

            return ValueTask.FromException<InterceptionResult>(
                new PostgresException("Имитирован конфликт сериализации при выдаче номера акта.", "ERROR", "ERROR", "40001"));
        }
    }

    private sealed class FailFirstSaveInterceptor : SaveChangesInterceptor
    {
        private int _remainingFailures;

        public int FailureCount { get; private set; }

        public void FailNextSave(int count = 1)
        {
            this._remainingFailures = count;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (this._remainingFailures == 0)
            {
                return ValueTask.FromResult(result);
            }

            this._remainingFailures--;
            this.FailureCount++;
            return ValueTask.FromException<InterceptionResult<int>>(
                new DbUpdateException("Имитирован временный конфликт при сохранении.", new InvalidOperationException()));
        }
    }
}
