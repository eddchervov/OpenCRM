namespace OpenCRM.WebApp.Models.Vehicles;

/// <summary> Модель страницы сценария работы с автомобилями с текстами интерфейса. </summary>
public class VehicleScenarioViewModel
{
    /// <summary> Код сценария страницы. </summary>
    public string ScenarioCode { get; set; } = string.Empty;

    /// <summary> Заголовок страницы. </summary>
    public string PageTitle { get; set; } = "Моя техника";

    /// <summary> Заголовок хлебной крошки. </summary>
    public string BreadcrumbTitle { get; set; } = "Моя техника";

    /// <summary> Подзаголовок страницы. </summary>
    public string Subtitle { get; set; } = "Храните карточки техники, историю обслуживания, расходы, документы и фотографии в одном месте.";

    /// <summary> Текст кнопки добавления. </summary>
    public string AddButtonText { get; set; } = "Добавить технику";

    /// <summary> Текст активной вкладки. </summary>
    public string ActiveTabText { get; set; } = "Активная техника";

    /// <summary> Плейсхолдер поля поиска. </summary>
    public string SearchPlaceholder { get; set; } = "Поиск по технике";

    /// <summary> Заголовок пустого списка. </summary>
    public string EmptyTitle { get; set; } = "Техника пока не добавлена";

    /// <summary> Текст для пустого списка. </summary>
    public string EmptyText { get; set; } = "Создайте первую карточку техники, чтобы начать вести историю обслуживания.";

    /// <summary> Текст загрузки. </summary>
    public string LoadingText { get; set; } = "Загружаем технику...";

    /// <summary> Текст для пустого архива. </summary>
    public string ArchiveEmptyText { get; set; } = "Здесь появится техника, которую вы удалите из активного списка.";

    /// <summary> Заголовок деталей записи. </summary>
    public string DetailsTitle { get; set; } = "Карточка техники";

    /// <summary> Заголовок модального окна создания. </summary>
    public string CreateModalTitle { get; set; } = "Создание техники";

    /// <summary> Заголовок модального окна редактирования. </summary>
    public string EditModalTitle { get; set; } = "Редактирование техники";

    /// <summary> Заголовок модального окна удаления. </summary>
    public string DeleteModalTitle { get; set; } = "Удалить технику";

    /// <summary> Заголовок модального окна восстановления. </summary>
    public string RestoreModalTitle { get; set; } = "Восстановление техники";

    /// <summary> Заголовок блока следующего шага. </summary>
    public string NextStepTitle { get; set; } = "Что сделать дальше";

    /// <summary> Текст блока следующего шага. </summary>
    public string NextStepText { get; set; } = "Начните с базовых действий, чтобы карточка техники стала полезной историей.";

    /// <summary> Список предлагаемых следующих действий. </summary>
    public IReadOnlyList<VehicleScenarioNextActionViewModel> NextActions { get; set; } =
    [
        new()
        {
            Kind = VehicleScenarioNextActionKind.AddMaintenance,
            IconCssClass = "fa fa-wrench",
            Title = "Добавить обслуживание",
            Text = "Запишите замену масла, ремонт, расходники или другую работу."
        },
        new()
        {
            Kind = VehicleScenarioNextActionKind.UploadPhoto,
            IconCssClass = "fa fa-camera",
            Title = "Добавить фото",
            Text = "Сохраните внешний вид техники или важные детали."
        },
        new()
        {
            Kind = VehicleScenarioNextActionKind.EditVehicle,
            IconCssClass = "fa fa-pencil",
            Title = "Редактировать технику",
            Text = "Уточните имя, номер, год выпуска или описание."
        }
    ];
}

/// <summary> Модель предлагаемого следующего действия на странице сценария. </summary>
public class VehicleScenarioNextActionViewModel
{
    /// <summary> Тип предлагаемого действия. </summary>
    public VehicleScenarioNextActionKind Kind { get; set; }

    /// <summary> CSS-класс иконки действия. </summary>
    public string IconCssClass { get; set; } = "fa fa-check";

    /// <summary> Заголовок действия. </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary> Описание действия. </summary>
    public string Text { get; set; } = string.Empty;
}

/// <summary> Типы предлагаемых следующих действий. </summary>
public enum VehicleScenarioNextActionKind
{
    /// <summary> Действие добавления записи об обслуживании. </summary>
    AddMaintenance,

    /// <summary> Действие загрузки фотографии. </summary>
    UploadPhoto,

    /// <summary> Действие редактирования автомобиля. </summary>
    EditVehicle
}
