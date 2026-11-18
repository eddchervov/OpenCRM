using OpenCRM.Domain;

namespace OpenCRM.Modules.Organizations.Export;

/// <summary> Явный состав полей версии 1: новые поля модели не попадают в выгрузку автоматически. </summary>
internal static class OrganizationExportFields
{
    internal static readonly IReadOnlyDictionary<Type, string> ByType = new Dictionary<Type, string>
    {
        [typeof(Brand)] = """
            Id Name Kind IsDeleted
            """,
        [typeof(Domain.Maintenance)] = """
            Id Version Status Type Date PlannedDate PlannedStartTime PlannedEndTime PlannedEndDate CompletedDate
            ReadyForHandoverAt HandedOverAt WorkActOrganizationId WorkActNumber Location EmployeeId Performer
            Mileage IntakeNotes IntakeMileage IntakeCompleteness IntakeVisibleDamage IntakeRecordedAt Comment
            DiscountAmount PaidAmount MaterialCostAmount LaborCostAmount DateTimeCreation UserId VehicleId
            BuyerClientId BuyerName BuyerPartyTypeCode BuyerInn BuyerKpp BuyerLegalAddress IsDeleted
            """,
        [typeof(MaintenanceDefect)] = """
            Id MaintenanceId VehicleDefectId OrganizationId UserId CreatedAt
            """,
        [typeof(MaintenanceDetail)] = """
            MaintenanceId DetailId Position Name Description Price VatRateCode EmployeeId EmployeeName
            CatalogItemId Quantity MaterialCatalogItemId MaterialName MaterialQuantity MaterialUnit
            MaterialUnitCost MaterialTotalCost
            """,
        [typeof(MaintenanceDocument)] = """
            Id MaintenanceId VehicleId OrganizationId UserId CreatedByUserId CreatedByDisplayName Kind Format
            IncludePhotos Title FileName ContentType Size Version CreatedAt IsDeleted
            """,
        [typeof(MaintenanceHistoryEvent)] = """
            Id MaintenanceId VehicleId OrganizationId UserId ActorDisplayName EventType Title Description
            PayloadJson CreatedAt
            """,
        [typeof(MaintenancePayment)] = """
            Id MaintenanceId Amount MethodCode PayerClientId PayerName PayerPartyTypeCode PayerInn PayerKpp
            PayerLegalAddress CreatedAt CreatedBy Comment IsOpeningBalance CancelledAt CancelledBy
            CancellationReason
            """,
        [typeof(MaintenancePhoto)] = """
            Stage MaintenanceId DetailId VehicleId Id Type CreatedAt IsDeleted Size ContentType OriginalFileName
            UserId
            """,
        [typeof(MaintenanceWorkEvent)] = """
            Id OperationId CommandId OrganizationId MaintenanceId EmployeeId MemberId ActorUserId PreviousStatus
            Status CreatedAt Reason PreviousVersion ResultVersion
            """,
        [typeof(MaintenanceWorkInterval)] = """
            Id OperationId EmployeeId StartedAt EndedAt
            """,
        [typeof(MaintenanceWorkOperation)] = """
            Id MaintenanceId OrganizationId EmployeeId Status StartedAt CompletedAt Version
            """,
        [typeof(MaintenanceWorkReport)] = """
            Id CommandId OrganizationId MaintenanceId DetailId EmployeeId MemberId ActorUserId Kind Text CreatedAt
            Decision DecisionReason DecidedByUserId DecidedAt ApprovedDetailId ApprovedPrice TimeAdjustmentMinutes
            """,
        [typeof(Model)] = """
            Id Name BrandId Kind IsDeleted
            """,
        [typeof(Organization)] = """
            Id Name Description LegalName Inn Kpp Ogrn LegalAddress ActualAddress Phone Email SignatoryPosition
            SignatoryName SignatoryBasis DocumentStyleCode MaintenanceWorkflowMode AllowWorkSelfAssignment
            SetupAutoOpenPending SetupStep SetupRevision SetupCompletedAt ShiftTimeZoneId WorkdayStart WorkdayEnd WorkingDays
            PrimaryScenarioCode OwnerUserId IsActive IsDeleted DeletionDate IsArchived ArchiveDate CreatedAt
            UpdatedAt
            """,
        [typeof(OrganizationAuditEvent)] = """
            Id OrganizationId ActorUserId EventType Title Description TargetType TargetId CreatedAt
            """,
        [typeof(OrganizationClient)] = """
            Id OrganizationId Name LastName FirstName MiddleName CompanyTypeCode Phone PartyTypeCode Inn Kpp LegalAddress Comment StatusCode ResponsibleUserId
            IsDeleted DeletionDate CreatedAt UpdatedAt
            """,
        [typeof(OrganizationClientHistoryEvent)] = """
            Id OrganizationClientId OrganizationId UserId ActorDisplayName EventType Title Description
            RelatedEntityType RelatedEntityId CreatedAt
            """,
        [typeof(OrganizationClientTag)] = """
            Id OrganizationId Name NormalizedName CreatedAt
            """,
        [typeof(OrganizationClientTagAssignment)] = """
            OrganizationClientId OrganizationClientTagId
            """,
        [typeof(OrganizationEmployee)] = """
            Id OrganizationId MemberId Name Phone IsActive Version
            """,
        [typeof(OrganizationEmployeeShift)] = """
            Id OrganizationId EmployeeId LocalStartDate PlanStartUtc PlanEndUtc PlannedBreakMinutes TimeZoneId
            IsPublished IsCancelled Version CreatedAt UpdatedAt
            """,
        [typeof(OrganizationMaterialMovement)] = """
            Id OrganizationId StockId CatalogItemId MaintenanceId OperationId EmployeeId MemberId CommandId
            TypeCode Quantity EnteredQuantity EnteredUnit ConversionFactor Unit UnitCost TotalCost
            QuantityBalanceAfter CostBalanceAfter SourceEntityType SourceEntityId Comment CreatedByUserId CreatedAt
            """,
        [typeof(OrganizationMaterialStock)] = """
            Id OrganizationId CatalogItemId Name Unit LegacyUnit PackageQuantity QuantityOnHand ReservedQuantity
            MinQuantity AverageUnitCost InventoryValue CreatedAt UpdatedAt
            """,
        [typeof(OrganizationMember)] = """
            Id OrganizationId UserId RoleCode InvitedByUserId CreatedAt JoinedAt
            """,
        [typeof(OrganizationRolePermissionOverride)] = """
            Id OrganizationId RoleCode PermissionCode IsEnabled CreatedAt UpdatedAt UpdatedByUserId
            """,
        [typeof(OrganizationScenarioCatalogItem)] = """
            Id OrganizationId ScenarioCode KindCode Code Name Description UnitPrice DurationMinutes
            DocumentKindCode SortOrder IsSystem IsActive CreatedAt UpdatedAt
            """,
        [typeof(OrganizationSchedulerTask)] = """
            Id OrganizationId VehicleId OrganizationClientId ResponsibleUserId CreatedByUserId Title Comment
            TypeCode PriorityCode StatusCode ProcessCode WorkflowStageCode LinkedDefectId LinkedMaintenanceId
            StartAt EndAt RecurrenceCode RecurrenceParentTaskId RecurrenceInterval RecurrenceUntil
            RecurrenceGeneratedUntil ReminderAt ReminderSentAt IsDeleted DeletedAt CreatedAt UpdatedAt ClosedAt
            """,
        [typeof(OrganizationSchedulerTaskState)] = """
            Id OrganizationId VehicleId SourceCode SourceEntityId TriggerKey StatusCode ProcessCode
            WorkflowStageCode DeferredUntil MaintenanceId UserId CreatedAt UpdatedAt ClosedAt
            """,
        [typeof(OrganizationServiceBundleItem)] = """
            BundleId ServiceId OrganizationId BundleKindCode ServiceKindCode Position
            """,
        [typeof(OrganizationWorkingDay)] = """
            OrganizationId DayOfWeek StartTime EndTime
            """,
        [typeof(OrganizationWorkingDayBreak)] = """
            OrganizationId DayOfWeek StartTime EndTime
            """,
        [typeof(Vehicle)] = """
            Id Kind MileageUnit BrandId ModelId ManualBrandName ManualModelName YearIssue Name Vin
            RegistrationPlate RegistrationCountryCode BodyNumber EngineNumber Color RegistrationCertificate CommissioningDate Description
            CurrentMileage CurrentMileageUpdatedAt DateTimeCreation IsDeleted DeletionDate IsArchived ArchiveDate
            UserId OrganizationId OrganizationClientId
            """,
        [typeof(VehicleCounterEntry)] = """
            Id VehicleId UserId MaintenanceId Value RecordedAt Comment SourceType IsDeleted DeletionDate
            """,
        [typeof(VehicleDefect)] = """
            Id OrganizationId VehicleId UserId Title Description SeverityCode StatusCode IsDeleted DeletionDate
            CreatedAt UpdatedAt ClosedAt
            """,
        [typeof(VehicleHistoryEvent)] = """
            Id VehicleId OrganizationId UserId ActorDisplayName EventType Title Description RelatedEntityType
            RelatedEntityId CreatedAt
            """,
        [typeof(VehiclePhoto)] = """
            VehicleId AlbumId IsPrimary Id Type CreatedAt IsDeleted Size ContentType OriginalFileName UserId
            """,
        [typeof(VehiclePhotoAlbum)] = """
            Id VehicleId UserId Title CreatedAt IsDeleted
            """,
    };
}
