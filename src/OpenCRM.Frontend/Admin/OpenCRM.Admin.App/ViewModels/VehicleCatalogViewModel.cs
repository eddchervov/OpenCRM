using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

public partial class VehicleCatalogViewModel(AdminSessionClient sessionClient) : CommunicationsViewModel(sessionClient)
{
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(this.ErrorMessage))
        {
            this.OnPropertyChanged(nameof(this.HasError));
        }
    }

    public IReadOnlyList<CatalogKindOption> KindOptions { get; } =
    [
        new(0, "Все виды техники"), new(1, "Легковые автомобили"), new(2, "Грузовая техника"),
        new(3, "Мотоциклы"), new(4, "Квадроциклы"), new(5, "Багги и UTV"),
        new(6, "Снегоходы"), new(7, "Гидроциклы"), new(8, "Лодки и катера"),
        new(9, "Спецтехника"), new(10, "Прицепы"), new(99, "Другие")
    ];

    [ObservableProperty] private IReadOnlyList<AdminCatalogBrand> _brands = [];
    [ObservableProperty] private IReadOnlyList<AdminCatalogModel> _models = [];
    [ObservableProperty] private AdminCatalogBrand? _selectedBrand;
    [ObservableProperty] private CatalogKindOption _selectedKind = new(0, "Все виды техники");
    [ObservableProperty] private CatalogKindOption _selectedEditKind = new(1, "Легковые автомобили");
    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private bool _isEditorOpen;
    [ObservableProperty] private bool _isBrandEditor;
    [ObservableProperty] private bool _isConfirmationOpen;
    [ObservableProperty] private string _editorTitle = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private int? _editingBrandId;
    [ObservableProperty] private int? _editingModelId;
    [ObservableProperty] private int? _editorParentBrandId;
    [ObservableProperty] private int? _brandToDelete;
    [ObservableProperty] private int? _modelToDelete;
    [ObservableProperty] private CatalogConfirmation _confirmation;
    [ObservableProperty] private string _message = string.Empty;

    public bool HasError => !string.IsNullOrWhiteSpace(this.ErrorMessage);
    public bool HasMessage => !string.IsNullOrWhiteSpace(this.Message);
    public bool HasBrands => this.Brands.Count > 0;
    public bool HasSelectedBrand => this.SelectedBrand != null;
    public bool HasModels => this.Models.Count > 0;
    public bool IsEmpty => !this.IsBusy && this.Brands.Count == 0 && !this.HasError;
    public bool HasNoSelectedBrand => this.SelectedBrand == null;
    public bool HasNoModels => this.SelectedBrand != null && this.Models.Count == 0;
    public string ConfirmationText => this.Confirmation switch
    {
        CatalogConfirmation.Seed => "Каталог будет дополнен базовыми марками и моделями. Ранее удалённые записи не восстанавливаются.",
        CatalogConfirmation.DeleteBrand => "Удалить выбранную марку? Если она используется, сервер отклонит операцию.",
        CatalogConfirmation.DeleteModel => "Удалить выбранную модель? Если она используется, сервер отклонит операцию.",
        _ => string.Empty
    };

    protected override async Task LoadPageAsync(CancellationToken cancellationToken)
    {
        var brands = await this.SessionClient.GetCatalogBrandsAsync(
            this.SelectedKind.Value == 0 ? null : this.SelectedKind.Value, this.Search.Trim(), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        this.Brands = brands;
        if (this.SelectedBrand is { } selected)
        {
            this.SelectedBrand = brands.FirstOrDefault(brand => brand.Id == selected.Id);
            this.Models = this.SelectedBrand == null
                ? []
                : await this.SessionClient.GetCatalogModelsAsync(this.SelectedBrand.Id, cancellationToken: cancellationToken);
        }
    }

    protected override void ApplyFilters() { }
    protected override void ResetFilters() => this.SelectedKind = this.KindOptions[0];

    protected override void ClearData()
    {
        this.Brands = [];
        this.Models = [];
        this.SelectedBrand = null;
        this.IsEditorOpen = false;
        this.IsConfirmationOpen = false;
        this.Message = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private Task SelectBrandAsync(AdminCatalogBrand brand) => this.RunAsync(async token =>
    {
        this.SelectedBrand = brand;
        this.Models = await this.SessionClient.GetCatalogModelsAsync(brand.Id, cancellationToken: token);
    });

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private void CloseModels()
    {
        this.SelectedBrand = null;
        this.Models = [];
        this.IsEditorOpen = false;
        this.IsConfirmationOpen = false;
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private void CreateBrand()
    {
        this.EditingBrandId = null;
        this.EditingModelId = null;
        this.EditorParentBrandId = null;
        this.EditorTitle = "Новая марка";
        this.IsBrandEditor = true;
        this.Name = string.Empty;
        this.SelectedEditKind = this.KindOptions.First(option => option.Value == (this.SelectedKind.Value == 0 ? 1 : this.SelectedKind.Value));
        this.IsEditorOpen = true;
    }

    [RelayCommand(CanExecute = nameof(CanEditModel))]
    private void CreateModel()
    {
        this.EditingBrandId = null;
        this.EditingModelId = null;
        this.EditorParentBrandId = this.SelectedBrand!.Id;
        this.EditorTitle = $"Новая модель · {this.SelectedBrand!.Name}";
        this.IsBrandEditor = false;
        this.Name = string.Empty;
        this.IsEditorOpen = true;
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private void EditBrand(AdminCatalogBrand brand)
    {
        this.EditingBrandId = brand.Id;
        this.EditingModelId = null;
        this.EditorParentBrandId = null;
        this.EditorTitle = "Изменить марку";
        this.IsBrandEditor = true;
        this.Name = brand.Name;
        this.SelectedEditKind = this.KindOptions.First(option => option.Value == brand.Kind);
        this.IsEditorOpen = true;
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private void EditModel(AdminCatalogModel model)
    {
        this.EditingBrandId = null;
        this.EditingModelId = model.Id;
        this.EditorParentBrandId = model.BrandId;
        this.EditorTitle = $"Изменить модель · {this.SelectedBrand?.Name}";
        this.IsBrandEditor = false;
        this.Name = model.Name;
        this.IsEditorOpen = true;
    }

    private bool CanEditModel() => this.CanLoad() && this.SelectedBrand != null;

    private bool CanSave() => this.CanLoad() && !string.IsNullOrWhiteSpace(this.Name)
        && (this.IsBrandEditor || this.EditorParentBrandId != null || this.EditingModelId != null);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync() => this.RunAsync(async token =>
    {
        if (this.IsBrandEditor)
        {
            await this.SessionClient.SaveCatalogBrandAsync(this.Name, this.SelectedEditKind.Value, this.EditingBrandId, token);
        }
        else
        {
            await this.SessionClient.SaveCatalogModelAsync(this.EditorParentBrandId ?? this.SelectedBrand!.Id,
                this.Name, this.EditingModelId, token);
        }

        this.IsEditorOpen = false;
        this.Message = "Запись каталога сохранена.";
        await this.LoadPageAsync(token);
    });

    [RelayCommand]
    private void CancelEdit() => this.IsEditorOpen = false;

    [RelayCommand]
    private void RequestSeed() => this.SetConfirmation(CatalogConfirmation.Seed);

    [RelayCommand]
    private void RequestDeleteBrand(AdminCatalogBrand brand)
    {
        this.BrandToDelete = brand.Id;
        this.SetConfirmation(CatalogConfirmation.DeleteBrand);
    }

    [RelayCommand]
    private void RequestDeleteModel(AdminCatalogModel model)
    {
        this.ModelToDelete = model.Id;
        this.SetConfirmation(CatalogConfirmation.DeleteModel);
    }

    [RelayCommand]
    private void CancelConfirmation() => this.IsConfirmationOpen = false;

    private bool CanConfirm() => this.CanLoad() && this.IsConfirmationOpen;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private Task ConfirmAsync() => this.RunAsync(async token =>
    {
        this.IsConfirmationOpen = false;
        if (this.Confirmation == CatalogConfirmation.Seed)
        {
            var result = await this.SessionClient.SeedVehicleCatalogAsync(token);
            this.Message = $"Добавлено марок: {result.CreatedBrands}, моделей: {result.CreatedModels}. Пропущено удалённых записей: {result.SkippedDeletedBrands + result.SkippedDeletedModels}.";
        }
        else if (this.Confirmation == CatalogConfirmation.DeleteBrand && this.BrandToDelete is { } brandId)
        {
            await this.SessionClient.DeleteCatalogBrandAsync(brandId, token);
            if (this.SelectedBrand?.Id == brandId)
            {
                this.SelectedBrand = null;
                this.Models = [];
            }

            this.Message = "Марка удалена.";
        }
        else if (this.Confirmation == CatalogConfirmation.DeleteModel && this.ModelToDelete is { } modelId)
        {
            await this.SessionClient.DeleteCatalogModelAsync(modelId, token);
            this.Message = "Модель удалена.";
        }

        await this.LoadPageAsync(token);
    });

    private void SetConfirmation(CatalogConfirmation confirmation)
    {
        this.Confirmation = confirmation;
        this.IsConfirmationOpen = true;
    }

    protected override void NotifyCommands()
    {
        base.NotifyCommands();
        this.SelectBrandCommand.NotifyCanExecuteChanged();
        this.CloseModelsCommand.NotifyCanExecuteChanged();
        this.CreateBrandCommand.NotifyCanExecuteChanged();
        this.CreateModelCommand.NotifyCanExecuteChanged();
        this.EditBrandCommand.NotifyCanExecuteChanged();
        this.EditModelCommand.NotifyCanExecuteChanged();
        this.SaveCommand.NotifyCanExecuteChanged();
        this.ConfirmCommand.NotifyCanExecuteChanged();
        this.OnPropertyChanged(nameof(HasError));
        this.OnPropertyChanged(nameof(HasMessage));
        this.OnPropertyChanged(nameof(HasBrands));
        this.OnPropertyChanged(nameof(HasSelectedBrand));
        this.OnPropertyChanged(nameof(HasModels));
        this.OnPropertyChanged(nameof(IsEmpty));
        this.OnPropertyChanged(nameof(HasNoSelectedBrand));
        this.OnPropertyChanged(nameof(HasNoModels));
        this.OnPropertyChanged(nameof(ConfirmationText));
    }

    partial void OnNameChanged(string value) => this.SaveCommand.NotifyCanExecuteChanged();
    partial void OnSelectedBrandChanged(AdminCatalogBrand? value)
    {
        this.CreateModelCommand.NotifyCanExecuteChanged();
        this.SaveCommand.NotifyCanExecuteChanged();
        this.OnPropertyChanged(nameof(HasSelectedBrand));
        this.OnPropertyChanged(nameof(HasNoSelectedBrand));
        this.OnPropertyChanged(nameof(HasNoModels));
    }
    partial void OnBrandsChanged(IReadOnlyList<AdminCatalogBrand> value)
    {
        this.OnPropertyChanged(nameof(HasBrands));
        this.OnPropertyChanged(nameof(IsEmpty));
    }
    partial void OnModelsChanged(IReadOnlyList<AdminCatalogModel> value)
    {
        this.OnPropertyChanged(nameof(HasModels));
        this.OnPropertyChanged(nameof(HasNoModels));
    }
    partial void OnIsBrandEditorChanged(bool value) => this.SaveCommand.NotifyCanExecuteChanged();
    partial void OnIsConfirmationOpenChanged(bool value) => this.ConfirmCommand.NotifyCanExecuteChanged();
}

public sealed record CatalogKindOption(int Value, string Label)
{
    public override string ToString() => this.Label;
}

public enum CatalogConfirmation
{
    Seed,
    DeleteBrand,
    DeleteModel
}
