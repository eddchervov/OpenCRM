using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;

namespace OpenCRM.Domain;

/// <summary> Карточка техники пользователя. </summary>
public class Vehicle
{
    /// <summary> Идентификатор записи. </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary> Тип техники. </summary>
    public VehicleKind Kind { get; set; } = VehicleKind.PassengerCar;

    /// <summary> Единица учета пробега или наработки. </summary>
    public VehicleMileageUnit MileageUnit { get; set; } = VehicleMileageUnit.Kilometers;

    /// <summary> Идентификатор бренда из справочника. </summary>
    public int? BrandId { get; set; }

    /// <summary> Бренд из справочника. </summary>
    public Brand? Brand { get; set; }

    /// <summary> Идентификатор модели из справочника. </summary>
    public int? ModelId { get; set; }

    /// <summary> Модель из справочника. </summary>
    public Model? Model { get; set; }

    /// <summary> Бренд, введенный вручную. </summary>
    public string? ManualBrandName { get; set; }

    /// <summary> Модель, введенная вручную. </summary>
    public string? ManualModelName { get; set; }

    /// <summary> Год выпуска. </summary>
    public int? YearIssue { get; set; }

    /// <summary> Пользовательское имя или номер. </summary>
    public string? Name { get; set; }

    /// <summary> VIN или серийный номер техники. </summary>
    public string? Vin { get; set; }

    /// <summary> Регистрационный номер техники. </summary>
    public string? RegistrationPlate { get; set; }

    /// <summary> Номер кузова или рамы. </summary>
    public string? BodyNumber { get; set; }

    /// <summary> Номер двигателя. </summary>
    public string? EngineNumber { get; set; }

    /// <summary> Цвет техники. </summary>
    public string? Color { get; set; }

    /// <summary> Номер СТС или иного регистрационного документа. </summary>
    public string? RegistrationCertificate { get; set; }

    /// <summary> Дата ввода техники в эксплуатацию. </summary>
    public DateTime? CommissioningDate { get; set; }

    /// <summary> Описание техники. </summary>
    public string? Description { get; set; }

    /// <summary> Текущее значение пробега или наработки. </summary>
    public int? CurrentMileage { get; set; }

    /// <summary> Дата последнего обновления текущего значения пробега или наработки. </summary>
    public DateTime? CurrentMileageUpdatedAt { get; set; }

    /// <summary> Дата создания записи. </summary>
    public DateTime DateTimeCreation { get; set; }

    /// <summary> Признак мягкого удаления. </summary>
    public bool IsDeleted { get; set; }

    /// <summary> Дата удаления. </summary>
    public DateTime? DeletionDate { get; set; }

    /// <summary> Признак архива. </summary>
    public bool IsArchived { get; set; }

    /// <summary> Дата архивации. </summary>
    public DateTime? ArchiveDate { get; set; }

    /// <summary> Идентификатор владельца. </summary>
    public string UserId { get; set; } = default!;

    /// <summary> Владелец записи. </summary>
    public virtual ApplicationUser User { get; set; } = default!;

    /// <summary> Организация, к которой относится техника. Null означает личную технику пользователя. </summary>
    public Guid? OrganizationId { get; set; }

    /// <summary> Организация, к которой относится техника. </summary>
    public virtual Organization? Organization { get; set; }

    /// <summary> Клиент организации, к которому относится техника. Null означает, что техника не привязана к клиенту. </summary>
    public Guid? OrganizationClientId { get; set; }

    /// <summary> Клиент организации, к которому относится техника. </summary>
    public virtual OrganizationClient? OrganizationClient { get; set; }

    /// <summary> История обслуживаний. </summary>
    public virtual ICollection<Maintenance> Maintenance { get; set; } = new HashSet<Maintenance>();

    /// <summary> История неисправностей. </summary>
    public virtual ICollection<VehicleDefect> Defects { get; set; } = new HashSet<VehicleDefect>();

    /// <summary> История обновлений счетчика. </summary>
    public virtual ICollection<VehicleCounterEntry> CounterEntries { get; set; } = new HashSet<VehicleCounterEntry>();

    /// <summary> Журнал событий техники. </summary>
    public virtual ICollection<VehicleHistoryEvent> HistoryEvents { get; set; } = new HashSet<VehicleHistoryEvent>();

    /// <summary> Фотографии техники. </summary>
    public virtual ICollection<VehiclePhoto> Photos { get; set; } = new HashSet<VehiclePhoto>();

    /// <summary> Альбомы фотографий техники. </summary>
    public virtual ICollection<VehiclePhotoAlbum> PhotoAlbums { get; set; } = new HashSet<VehiclePhotoAlbum>();

    /// <summary> Переводит карточку техники в архив на указанную дату. </summary>
    public void Archive(DateTime archiveDate)
    {
        IsArchived = true;
        ArchiveDate = archiveDate;
        IsDeleted = false;
    }

    /// <summary> Помечает карточку техники удалённой с указанной датой. </summary>
    public void Delete(DateTime deletionDate)
    {
        IsDeleted = true;
        DeletionDate = deletionDate;
        IsArchived = false;
    }

    /// <summary> Восстанавливает карточку техники после удаления. </summary>
    public void Restore()
    {
        IsDeleted = false;
        DeletionDate = null;
        IsArchived = false;
        ArchiveDate = null;
    }
}

