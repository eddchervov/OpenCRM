using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.ViewModels;

public partial class TestDataViewModel : CommunicationsViewModel
{
    public TestDataViewModel(AdminSessionClient sessionClient) : base(sessionClient)
    {
        this.PageSize = 20;
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(this.ErrorMessage))
        {
            this.OnPropertyChanged(nameof(this.HasError));
        }
    }

    [ObservableProperty] private IReadOnlyList<AdminTestDataOrganization> _organizations = [];
    [ObservableProperty] private IReadOnlyList<AdminTestDataVehicle> _vehicles = [];
    [ObservableProperty] private AdminTestDataOrganization? _selectedOrganization;
    [ObservableProperty] private AdminTestDataVehicle? _selectedVehicle;
    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private TestDataAction _action;
    [ObservableProperty] private int _count = 10;
    [ObservableProperty] private MaintenanceModeOption _selectedMaintenanceMode = new("completed", "native.billing.zavershyonnye");
    [ObservableProperty] private bool _isConfirmationOpen;
    [ObservableProperty] private bool _isGenerationOutcomeUnknown;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _isLegacyOrganizationPaging;
    private string _appliedSearch = string.Empty;
    private Guid? _attemptedOrganizationId;
    private Guid? _attemptedVehicleId;
    private string? _messageTranslationKey;
    private bool _isSettingLocalizedMessage;

    public IReadOnlyList<MaintenanceModeOption> MaintenanceModes { get; } =
    [new("completed", "native.billing.zavershyonnye"), new("planned", "native.testData.zaplanirovannye")];

    public bool HasError => !string.IsNullOrWhiteSpace(this.ErrorMessage);
    public bool HasMessage => !string.IsNullOrWhiteSpace(this.Message);
    public bool HasSelection => this.SelectedOrganization != null;
    public bool HasVehicle => this.SelectedVehicle != null;
    public bool CanStartGeneration => this.CanLoad() && !this.IsGenerationOutcomeUnknown;
    public string OutcomeWarning => this.IsGenerationOutcomeUnknown
        ? AdminLocalization.Instance["native.testData.otvet.na.sozdanie.ne.poluchen.i.rezultat.neizvesten.obnovite.dan"]
        : string.Empty;
    public bool IsEmpty => !this.IsBusy && this.Organizations.Count == 0 && !this.HasError;
    public bool HasNoVehicles => this.SelectedOrganization != null && this.Vehicles.Count == 0 && !this.IsBusy;
    public bool IsGeneratingVehicles => this.Action == TestDataAction.Vehicles;
    public bool IsGeneratingMaintenances => this.Action == TestDataAction.Maintenances;
    public bool IsGeneratingDefects => this.Action == TestDataAction.Defects;
    public int MaximumCount => this.Action == TestDataAction.Maintenances ? 300 : 100;
    public string ActionTitle => this.Action switch
    {
        TestDataAction.Vehicles => AdminLocalization.Instance["native.testData.sozdat.testovuyu.tekhniku"],
        TestDataAction.Maintenances => AdminLocalization.Instance["native.testData.sozdat.obsluzhivaniya"],
        TestDataAction.Defects => AdminLocalization.Instance["native.testData.sozdat.defekty"],
        _ => AdminLocalization.Instance["native.testData.testovye.dannye"]
    };
    public string ConfirmationText => AdminLocalization.Instance.Format("native.testData.budet.sozdano.zapisey.this.count.this.actiontitle.tolowerinvaria", this.Count, this.ActionTitle.ToLowerInvariant());

    public override void RefreshLocalizedValues()
    {
        base.RefreshLocalizedValues();
        foreach (var option in this.MaintenanceModes)
        {
            option.RefreshLocalizedValues();
        }

        this.SelectedMaintenanceMode.RefreshLocalizedValues();
        if (this._messageTranslationKey != null)
        {
            this.SetLocalizedMessage(this._messageTranslationKey);
        }
    }

    protected override async Task LoadPageAsync(CancellationToken cancellationToken)
    {
        if (this.SelectedOrganization is { } selected)
        {
            var organization = await this.SessionClient.GetTestDataOrganizationAsync(selected.Id, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            this.SelectedOrganization = organization;
            if (this.SelectedOrganization != null)
            {
                var vehicles = await this.SessionClient.GetTestDataVehiclesAsync(selected.Id, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                this.Vehicles = vehicles;
                this.SelectedVehicle = vehicles.FirstOrDefault(vehicle => vehicle.Id == this.SelectedVehicle?.Id);
                this.UpdateOrganizationInPage(organization!);
                return;
            }
        }

        var result = await this.SessionClient.GetTestDataOrganizationsPageAsync(
            this._appliedSearch, this.Page, this.PageSize, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        this.Organizations = result.Items;
        this.TotalCount = result.TotalCount;
        this.Page = result.Page;
        this.PageSize = result.PageSize;
        this.IsLegacyOrganizationPaging = result.IsLegacyResponse;
    }

    protected override void ApplyFilters() => this._appliedSearch = this.Search.Trim();
    protected override void ResetFilters() { }

    protected override void ClearData()
    {
        if (!this.IsActive)
        {
            this._appliedSearch = this.Search.Trim();
        }
        this.Organizations = [];
        this.TotalCount = 0;
        this.Vehicles = [];
        this.SelectedOrganization = null;
        this.SelectedVehicle = null;
        this.IsConfirmationOpen = false;
        this.Message = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private Task SelectOrganizationAsync(AdminTestDataOrganization organization) => this.RunAsync(async token =>
    {
        this.SelectedOrganization = organization;
        this.SelectedVehicle = null;
        this.Vehicles = await this.SessionClient.GetTestDataVehiclesAsync(organization.Id, token);
    });

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private void SelectVehicle(AdminTestDataVehicle vehicle) => this.SelectedVehicle = vehicle;

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private void CloseOrganization()
    {
        this.IsConfirmationOpen = false;
        this.SelectedOrganization = null;
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private void CloseVehicle()
    {
        this.IsConfirmationOpen = false;
        this.SelectedVehicle = null;
    }

    [RelayCommand(CanExecute = nameof(CanStartGeneration))]
    private void RequestVehicles() => this.OpenConfirmation(TestDataAction.Vehicles, 10);

    [RelayCommand(CanExecute = nameof(CanGenerateForVehicle))]
    private void RequestMaintenances() => this.OpenConfirmation(TestDataAction.Maintenances, 20);

    [RelayCommand(CanExecute = nameof(CanGenerateForVehicle))]
    private void RequestDefects() => this.OpenConfirmation(TestDataAction.Defects, 10);

    private bool CanGenerateForVehicle() => this.CanStartGeneration && this.SelectedOrganization != null && this.SelectedVehicle != null;

    private void OpenConfirmation(TestDataAction action, int defaultCount)
    {
        if (this.IsGenerationOutcomeUnknown)
        {
            return;
        }

        this.Action = action;
        this.Count = defaultCount;
        this.IsConfirmationOpen = true;
        this.OnPropertyChanged(nameof(ActionTitle));
        this.OnPropertyChanged(nameof(MaximumCount));
        this.OnPropertyChanged(nameof(IsGeneratingVehicles));
        this.OnPropertyChanged(nameof(IsGeneratingMaintenances));
        this.OnPropertyChanged(nameof(IsGeneratingDefects));
        this.OnPropertyChanged(nameof(ConfirmationText));
        this.ConfirmCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void CancelConfirmation() => this.IsConfirmationOpen = false;

    private bool CanConfirm() => this.CanStartGeneration && this.IsConfirmationOpen && this.Count is >= 1
        && this.Count <= this.MaximumCount && this.SelectedOrganization != null
        && (this.Action == TestDataAction.Vehicles || this.SelectedVehicle != null);

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private Task ConfirmAsync() => this.RunAsync(async token =>
    {
        var organizationId = this.SelectedOrganization!.Id;
        var vehicleId = this.SelectedVehicle?.Id;
        this._attemptedOrganizationId = organizationId;
        this._attemptedVehicleId = vehicleId;
        this.IsGenerationOutcomeUnknown = true;
        this.IsConfirmationOpen = false;
        AdminTestDataGenerationResult result = this.Action switch
        {
            TestDataAction.Vehicles => await this.SessionClient.GenerateTestVehiclesAsync(
                organizationId, this.Count, token),
            TestDataAction.Maintenances => await this.SessionClient.GenerateTestMaintenancesAsync(
                organizationId, vehicleId!.Value, this.Count, this.SelectedMaintenanceMode.Value, token),
            TestDataAction.Defects => await this.SessionClient.GenerateTestDefectsAsync(
                organizationId, vehicleId!.Value, this.Count, token),
            _ => throw new InvalidOperationException(AdminLocalization.Instance["native.testData.ne.vybrano.deystvie.generatsii"])
        };

        this.IsGenerationOutcomeUnknown = false;
        this._attemptedOrganizationId = null;
        this._attemptedVehicleId = null;
        token.ThrowIfCancellationRequested();
        this.Message = result.Message;
        var organization = await this.SessionClient.GetTestDataOrganizationAsync(organizationId, token);
        token.ThrowIfCancellationRequested();
        this.SelectedOrganization = organization;
        if (this.SelectedOrganization == null)
        {
            this.Vehicles = [];
            this.SelectedVehicle = null;
            await this.LoadPageAsync(token);
            return;
        }

        this.UpdateOrganizationInPage(organization!);
        var vehicles = await this.SessionClient.GetTestDataVehiclesAsync(organizationId, token);
        token.ThrowIfCancellationRequested();
        this.Vehicles = vehicles;
        this.SelectedVehicle = vehicleId.HasValue ? vehicles.FirstOrDefault(vehicle => vehicle.Id == vehicleId.Value) : null;
    });

    private bool CanRefreshUnknownOutcome() => this.CanLoad() && this.IsGenerationOutcomeUnknown;

    [RelayCommand(CanExecute = nameof(CanRefreshUnknownOutcome))]
    private Task RefreshUnknownOutcomeAsync() => this.RunAsync(async token =>
    {
        if (this._attemptedOrganizationId is not { } organizationId)
        {
            return;
        }

        var organization = await this.SessionClient.GetTestDataOrganizationAsync(organizationId, token);
        token.ThrowIfCancellationRequested();
        if (organization == null)
        {
            this.SetLocalizedErrorMessage("native.testData.iskhodnuyu.organizatsiyu.ne.udalos.nayti.rezultat.generatsii.ost");
            return;
        }

        var vehicles = await this.SessionClient.GetTestDataVehiclesAsync(organizationId, token);
        token.ThrowIfCancellationRequested();
        if (this._attemptedVehicleId is { } vehicleId
            && vehicles.All(vehicle => vehicle.Id != vehicleId))
        {
            this.SetLocalizedErrorMessage("native.testData.iskhodnuyu.tekhniku.ne.udalos.nayti.v.organizatsii.rezultat.gene");
            return;
        }

        this.UpdateOrganizationInPage(organization);
        this.SelectedOrganization = organization;
        this.Vehicles = vehicles;
        this.SelectedVehicle = this._attemptedVehicleId is { } selectedVehicleId
            ? vehicles.FirstOrDefault(vehicle => vehicle.Id == selectedVehicleId)
            : null;
        this.IsGenerationOutcomeUnknown = false;
        this._attemptedOrganizationId = null;
        this._attemptedVehicleId = null;
        this.SetLocalizedMessage("native.testData.dannye.obnovleny.proverte.kolichestva.pered.povtornym.sozdaniem");
    });

    private void SetLocalizedMessage(string key)
    {
        this._messageTranslationKey = key;
        this._isSettingLocalizedMessage = true;
        this.Message = AdminLocalization.Instance[key];
        this._isSettingLocalizedMessage = false;
    }

    private void UpdateOrganizationInPage(AdminTestDataOrganization organization)
    {
        this.Organizations = this.Organizations.Select(item => item.Id == organization.Id ? organization : item).ToArray();
    }

    protected override void NotifyCommands()
    {
        base.NotifyCommands();
        this.SelectOrganizationCommand.NotifyCanExecuteChanged();
        this.SelectVehicleCommand.NotifyCanExecuteChanged();
        this.CloseOrganizationCommand.NotifyCanExecuteChanged();
        this.CloseVehicleCommand.NotifyCanExecuteChanged();
        this.RequestVehiclesCommand.NotifyCanExecuteChanged();
        this.RequestMaintenancesCommand.NotifyCanExecuteChanged();
        this.RequestDefectsCommand.NotifyCanExecuteChanged();
        this.ConfirmCommand.NotifyCanExecuteChanged();
        this.RefreshUnknownOutcomeCommand.NotifyCanExecuteChanged();
        this.OnPropertyChanged(nameof(HasError));
        this.OnPropertyChanged(nameof(HasMessage));
        this.OnPropertyChanged(nameof(HasSelection));
        this.OnPropertyChanged(nameof(HasVehicle));
        this.OnPropertyChanged(nameof(IsEmpty));
        this.OnPropertyChanged(nameof(HasNoVehicles));
        this.OnPropertyChanged(nameof(ConfirmationText));
        this.OnPropertyChanged(nameof(CanStartGeneration));
        this.OnPropertyChanged(nameof(OutcomeWarning));
    }

    partial void OnSelectedOrganizationChanged(AdminTestDataOrganization? value)
    {
        if (value == null)
        {
            this.Vehicles = [];
            this.SelectedVehicle = null;
        }

        this.OnPropertyChanged(nameof(HasSelection));
        this.NotifyCommands();
    }
    partial void OnSelectedVehicleChanged(AdminTestDataVehicle? value)
    {
        this.OnPropertyChanged(nameof(HasVehicle));
        this.OnPropertyChanged(nameof(HasNoVehicles));
        this.NotifyCommands();
    }
    partial void OnCountChanged(int value)
    {
        this.OnPropertyChanged(nameof(ConfirmationText));
        this.ConfirmCommand.NotifyCanExecuteChanged();
    }
    partial void OnIsConfirmationOpenChanged(bool value) => this.ConfirmCommand.NotifyCanExecuteChanged();
    partial void OnIsGenerationOutcomeUnknownChanged(bool value)
    {
        this.OnPropertyChanged(nameof(CanStartGeneration));
        this.OnPropertyChanged(nameof(OutcomeWarning));
        this.NotifyCommands();
    }
    partial void OnOrganizationsChanged(IReadOnlyList<AdminTestDataOrganization> value) => this.OnPropertyChanged(nameof(IsEmpty));
    partial void OnVehiclesChanged(IReadOnlyList<AdminTestDataVehicle> value) => this.OnPropertyChanged(nameof(HasNoVehicles));

    partial void OnMessageChanged(string value)
    {
        if (!this._isSettingLocalizedMessage)
        {
            this._messageTranslationKey = null;
        }
    }
}

public enum TestDataAction
{
    None,
    Vehicles,
    Maintenances,
    Defects
}

public sealed class MaintenanceModeOption(string value, string localizationKey) : ObservableObject
{
    public string Value { get; } = value;

    public string Label => AdminLocalization.Instance[localizationKey];

    public void RefreshLocalizedValues() => this.OnPropertyChanged(nameof(this.Label));

    public override string ToString() => this.Label;

    public override bool Equals(object? obj) => obj is MaintenanceModeOption other
        && string.Equals(this.Value, other.Value, StringComparison.Ordinal);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(this.Value);
}
