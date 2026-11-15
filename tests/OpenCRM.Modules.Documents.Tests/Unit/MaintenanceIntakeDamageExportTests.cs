using Moq;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Maintenance.Contracts.Photos;
using OpenCRM.Services.Interfaces.Documents.Dtos;
using OpenCRM.Services.Interfaces.Pdf.Dtos;

namespace OpenCRM.Modules.Documents.Tests.Unit;

public sealed class MaintenanceIntakeDamageExportTests
{
    [Fact]
    public async Task UnavailableHistoricalDiagramReturnsAnErrorWithoutCallingTheRenderer()
    {
        var fixture = new DocumentExportTestFixture();
        fixture.IntakeDamages.Setup(service => service.GetDamages(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>()))
            .ReturnsAsync(new MaintenanceIntakeDamagesDto
            {
                DiagramVersion = "archived-unavailable",
                Damages = [new MaintenanceIntakeDamageDto { Number = 1, ViewCode = "rear" }]
            });

        var result = await fixture.Pdf.PrintMaintenanceIntake(DocumentExportTests.UserId, DocumentExportTests.MaintenanceId,
            DocumentExportTests.OrganizationId);

        Assert.Equal(DocumentExportOutcome.InvalidRequest, result.Outcome);
        fixture.PdfGenerator.Verify(generator => generator.GenerateMaintenanceIntakePdfAsync(It.IsAny<MaintenanceIntakePdfModel>()), Times.Never);
    }

    [Fact]
    public async Task IntakeIncludesNumberedDamagesAndLabelsRelatedPhotosWithoutLosingLegacyNotes()
    {
        var fixture = new DocumentExportTestFixture();
        var photoId = Guid.NewGuid();
        fixture.AllowPhotoAccess();
        fixture.ConfigurePhoto([1, 2, 3]);
        fixture.SetMaintenancePhotos(new MaintenancePhotoDto(photoId, "", MaintenancePhotoStage.Before, DateTime.UtcNow));
        fixture.IntakeDamages.Setup(service => service.GetDamages(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>()))
            .ReturnsAsync(new MaintenanceIntakeDamagesDto
            {
                DiagramVersion = "sedan-v1",
                Damages = [new MaintenanceIntakeDamageDto
                {
                    Number = 1, ViewCode = "left", DamageType = "scratch", Description = "На задней двери", PhotoIds = [photoId]
                }]
            });
        MaintenanceIntakePdfModel? captured = null;
        fixture.PdfGenerator.Setup(generator => generator.GenerateMaintenanceIntakePdfAsync(It.IsAny<MaintenanceIntakePdfModel>()))
            .Callback<MaintenanceIntakePdfModel>(model => captured = model).ReturnsAsync([1]);

        var result = await fixture.Pdf.PrintMaintenanceIntake(DocumentExportTests.UserId, DocumentExportTests.MaintenanceId,
            DocumentExportTests.OrganizationId);

        Assert.Equal(DocumentExportOutcome.File, result.Outcome);
        Assert.NotNull(captured);
        Assert.Equal("sedan-v1", captured.DiagramVersion);
        var damage = Assert.Single(captured.Damages);
        Assert.Equal(1, damage.Number);
        Assert.Equal("Левый борт", damage.ViewName);
        Assert.Equal("Царапина", damage.DamageTypeName);
        Assert.Equal("На задней двери", damage.Description);
        Assert.Equal(fixture.Maintenance.IntakeVisibleDamage, captured.VisibleDamage);
        Assert.Equal("Повреждения № 1", Assert.Single(captured.BeforePhotos).Caption);
    }
}
