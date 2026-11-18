namespace OpenCRM.Services.Interfaces.Imports;

/// <summary>Стабильные коды сообщений проверки строк импорта.</summary>
public static class ImportRowMessageCodes
{
    public const string Excluded = "rowExcluded";
    public const string WillCreate = "rowWillCreate";
    public const string CreateSeparateClient = "createSeparateClient";
    public const string ClientInvalid = "clientInvalid";
    public const string SelectedClientUnavailable = "selectedClientUnavailable";
    public const string SelectedClientLinked = "selectedClientLinked";
    public const string ClientPossibleDuplicate = "clientPossibleDuplicate";
    public const string DuplicateClientExternalId = "duplicateClientExternalId";
    public const string DuplicateClientInFile = "duplicateClientInFile";
    public const string RegistrationCountryInvalid = "registrationCountryInvalid";
    public const string ForeignPlateInvalid = "foreignPlateInvalid";
    public const string VehicleInvalid = "vehicleInvalid";
    public const string VehicleIdentityRequired = "vehicleIdentityRequired";
    public const string VehicleIdentityConflict = "vehicleIdentityConflict";
    public const string VehicleAlreadyExists = "vehicleAlreadyExists";
    public const string DuplicateVehicleInFile = "duplicateVehicleInFile";
    public const string VehicleClientReferenceInvalid = "vehicleClientReferenceInvalid";
    public const string VehicleClientAmbiguous = "vehicleClientAmbiguous";
    public const string ClientLimitExceeded = "clientLimitExceeded";
    public const string VehicleLimitExceeded = "vehicleLimitExceeded";
    public const string DuplicateVehicleExternalId = "duplicateVehicleExternalId";
    public const string VehicleNotReady = "vehicleNotReady";
    public const string ExternalIdentityTooLong = "externalIdentityTooLong";
    public const string CrmClientInvalid = "crmClientInvalid";
    public const string CrmClientDuplicate = "crmClientDuplicate";
    public const string CrmVehicleInvalid = "crmVehicleInvalid";
    public const string CrmVehicleDuplicate = "crmVehicleDuplicate";
    public const string CrmVehicleClientAmbiguous = "crmVehicleClientAmbiguous";
    public const string StatusCreated = "statusCreated";
    public const string AlreadyImported = "alreadyImported";
    public const string IdentityConflict = "identityConflict";
    public const string IdentityExternalIdDuplicate = "identityExternalIdDuplicate";
    public const string SourceExternalIdRequired = "sourceExternalIdRequired";
    public const string ChildIdentityAlreadyImported = "childIdentityAlreadyImported";
    public const string ChildIdentityConflict = "childIdentityConflict";
}
