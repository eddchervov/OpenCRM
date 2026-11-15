using OpenCRM.Domain;
using OpenCRM.Services.Interfaces.Documents.Dtos;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Services.Interfaces.Documents;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Pdf;
using OpenCRM.Services.Interfaces.Pdf.Dtos;
using OpenCRM.Modules.Vehicles.Contracts;

namespace OpenCRM.Modules.Documents;

/// <summary> Сценарий печати документа приёма обслуживания. </summary>
public sealed class MaintenanceIntakeWorkflow
{
    /// <summary> Зависимость сервиса генерации PDF. </summary>
    private readonly IPdfGeneratorService _pdfGeneratorService;

    /// <summary> Зависимость сервиса запросов обслуживаний. </summary>
    private readonly IMaintenanceQueryService _maintenanceQueryService;

    /// <summary> Зависимость сервиса запросов техники. </summary>
    private readonly IVehicleQueryService _vehicleService;

    /// <summary> Зависимость сервиса запросов организаций. </summary>
    private readonly IOrganizationQueryService _organizationService;

    /// <summary> Зависимость проверки доступа к экспорту документов. </summary>
    private readonly DocumentExportAccess _documentExportAccess;

    /// <summary> Зависимость загрузчика фотографий документов. </summary>
    private readonly DocumentPhotoLoader _documentPhotoLoader;

    /// <summary> Чтение повреждений, зафиксированных при осмотре. </summary>
    private readonly IMaintenanceIntakeDamageService _intakeDamageService;

    /// <summary> Создаёт сценарий с его зависимостями. </summary>
    public MaintenanceIntakeWorkflow(
        IPdfGeneratorService pdfGeneratorService,
        IMaintenanceQueryService maintenanceQueryService,
        IVehicleQueryService vehicleService,
        IOrganizationQueryService organizationService,
        DocumentExportAccess documentExportAccess,
        DocumentPhotoLoader documentPhotoLoader,
        IMaintenanceIntakeDamageService intakeDamageService)
    {
        this._pdfGeneratorService = pdfGeneratorService;
        this._maintenanceQueryService = maintenanceQueryService;
        this._vehicleService = vehicleService;
        this._organizationService = organizationService;
        this._documentExportAccess = documentExportAccess;
        this._documentPhotoLoader = documentPhotoLoader;
        this._intakeDamageService = intakeDamageService;
    }

    /// <summary> Печатает документ приёма обслуживания в PDF. </summary>
    public async Task<DocumentExportResult> PrintMaintenanceIntake(string userId, Guid maintenanceId, Guid organizationId)
    {
        var access = await this._documentExportAccess.GetWorkspaceAccessAsync(userId, organizationId);
        if (!access.ExportsEnabled)
        {
            return DocumentExportResult.AccessDenied("У вас нет доступа к печати акта приёмки.");
        }

        var hasHistoryAccess = await this._documentExportAccess.HasIntakeHistoryAccessAsync(userId, organizationId);
        var canIncludePhotos = hasHistoryAccess
            || await this._documentExportAccess.HasPdfAccessAsync(userId, organizationId, includePhotos: true);

        if (!canIncludePhotos && !await this._documentExportAccess.HasPdfAccessAsync(userId, organizationId, includePhotos: false))
        {
            return DocumentExportResult.AccessDenied("У вас нет доступа к печати акта приёмки.");
        }

        var maintenance = await this._maintenanceQueryService.GetById(userId, organizationId, maintenanceId);
        if (maintenance == null)
        {
            return DocumentExportResult.NotFound("Обслуживание не найдено.");
        }

        if (!maintenance.IntakeRecordedAt.HasValue)
        {
            return DocumentExportResult.NotFound("Акт приёмки ещё не заполнен.");
        }

        var vehicle = await this._vehicleService.GetById(userId, organizationId, maintenance.VehicleId);
        if (vehicle == null)
        {
            return DocumentExportResult.NotFound("Автомобиль не найден.");
        }

        var beforePhotos = canIncludePhotos
            ? await this._documentPhotoLoader.LoadIntakeBeforePhotosAsync(userId, organizationId, maintenanceId)
            : [];

        var intake = await this._intakeDamageService.GetDamages(userId, organizationId, maintenanceId);
        if (intake.Damages.Count > 0 && !MaintenanceIntakeDiagramVersions.IsSupported(intake.DiagramVersion))
        {
            return DocumentExportResult.InvalidRequest("Сохранённая версия схемы осмотра недоступна. Нельзя сформировать акт с другой схемой.");
        }

        var damages = intake.Damages.Select(damage =>
        {
            var view = damage.ViewCode switch { "front" => "Спереди", "rear" => "Сзади", "left" => "Левый борт", _ => "Правый борт" };
            var type = damage.DamageType switch { "scratch" => "Царапина", "chip" => "Скол", "dent" => "Вмятина", _ => "Другое" };

            return new MaintenanceIntakeDamagePdfInfo
            {
                Number = damage.Number, ViewCode = damage.ViewCode, ViewName = view, DamageTypeName = type,
                X = damage.X, Y = damage.Y, Description = damage.Description
            };
        }).ToArray();

        foreach (var photo in beforePhotos)
        {
            var numbers = intake.Damages.Where(damage => photo.PhotoId.HasValue && damage.PhotoIds.Contains(photo.PhotoId.Value))
                .Select(damage => damage.Number).ToArray();
            if (numbers.Length > 0)
            {
                photo.Caption = $"Повреждения № {string.Join(", ", numbers)}";
            }
        }

        var model = new MaintenanceIntakePdfModel
        {
            OrganizationName = (await this._organizationService.GetByIdAsync(userId, organizationId)).Name,
            ClientName = vehicle.OrganizationClientName,
            Performer = maintenance.Performer,
            Vehicle = new VehicleInfo
            {
                BrandName = vehicle.BrandName,
                ModelName = vehicle.ModelName,
                RegistrationPlate = vehicle.RegistrationPlate,
                MileageUnit = vehicle.MileageUnit,
                YearIssue = vehicle.YearIssue,
                Name = vehicle.Name,
                Description = vehicle.Description
            },
            MaintenanceReference = maintenance.Id.ToString(),
            RecordedDate = maintenance.IntakeRecordedAt.Value.ToString("dd.MM.yyyy HH:mm 'UTC'"),
            GeneratedDate = DateTime.UtcNow.ToString("dd.MM.yyyy HH:mm 'UTC'"),
            Mileage = maintenance.IntakeMileage,
            Completeness = maintenance.IntakeCompleteness,
            VisibleDamage = maintenance.IntakeVisibleDamage,
            Damages = damages,
            DiagramVersion = intake.DiagramVersion,
            Notes = maintenance.IntakeNotes,
            BeforePhotos = beforePhotos
        };
        var pdfBytes = await this._pdfGeneratorService.GenerateMaintenanceIntakePdfAsync(model);

        return DocumentExportResult.File(pdfBytes, "application/pdf");
    }
}
