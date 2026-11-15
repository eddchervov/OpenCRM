using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCRM.Admin.Api;

namespace OpenCRM.Admin.App.ViewModels;

public partial class BillingViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLedgerListVisible))]
    [NotifyPropertyChangedFor(nameof(IsLedgerDetailVisible))]
    [NotifyPropertyChangedFor(nameof(IsPaymentTabSelected))]
    [NotifyPropertyChangedFor(nameof(IsSubscriptionTabSelected))]
    private bool _isPurchasesTab;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOperationMessage))]
    private string _operationMessage = string.Empty;

    /// <summary> Создаёт рабочее пространство биллинга с отдельной очередью чеков. </summary>
    public BillingViewModel(AdminSessionClient sessionClient)
    {
        this._sessionClient = sessionClient;
        this.PaymentActions = new PaymentActionsViewModel(sessionClient);
        this.Purchases = new PurchasesViewModel(sessionClient);
        this.PaymentActions.SessionExpired += () => this.SessionExpired?.Invoke();
        this.Purchases.SessionExpired += () => this.SessionExpired?.Invoke();
        this.PaymentActions.AccessDenied += this.RefreshChildPermissionsAsync;
        this.Purchases.AccessDenied += this.RefreshChildPermissionsAsync;
        this.PaymentActions.PaymentChanged += this.RefreshAfterPaymentChangeAsync;
        this.PaymentActions.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(PaymentActionsViewModel.IsBusy))
            {
                if (this.PaymentActions.IsBusy)
                {
                    this.OperationMessage = string.Empty;
                }

                this.NotifyCommands();
            }
        };
    }

    public PaymentActionsViewModel PaymentActions { get; }

    public PurchasesViewModel Purchases { get; }

    public bool IsLedgerListVisible => !this.IsPurchasesTab && !this.IsDetailOpen;

    public bool IsLedgerDetailVisible => !this.IsPurchasesTab && this.IsDetailOpen;

    public bool IsPaymentTabSelected => !this.IsPurchasesTab && this.IsPaymentsTab;

    public bool IsSubscriptionTabSelected => !this.IsPurchasesTab && !this.IsPaymentsTab;

    public bool HasOperationMessage => this.OperationMessage.Length > 0;

    public event Func<Task>? PermissionsRefreshRequested;

    public void RefreshAccess()
    {
        this.PaymentActions.RefreshAccess();
        this.Purchases.RefreshAccess();
    }

    private Task RefreshChildPermissionsAsync() => this.PermissionsRefreshRequested?.Invoke() ?? Task.CompletedTask;

    private async Task RefreshAfterPaymentChangeAsync(CancellationToken cancellationToken)
    {
        if (this._isActive && !cancellationToken.IsCancellationRequested)
        {
            this.OperationMessage = this.PaymentActions.SuccessMessage;
        }

        await this.RefreshAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task ShowPurchasesAsync()
    {
        if (!this._isActive || this.IsPurchasesTab)
        {
            return;
        }

        var navigationVersion = ++this._queryRestartVersion;
        await this.CancelRefreshAsync();
        this.OpenDetailCommand.Cancel();
        await this.PaymentActions.DeactivateAsync(true);
        if (this.OpenDetailCommand.ExecutionTask is { } detailOperation)
        {
            await detailOperation;
        }

        if (!this._isActive || navigationVersion != this._queryRestartVersion)
        {
            return;
        }

        this.IsPurchasesTab = true;
        this.IsDetailOpen = false;
        this.Detail = null;
        this._detailId = null;
        await this.Purchases.ActivateAsync();
    }

    partial void OnIsDetailOpenChanged(bool value)
    {
        this.OnPropertyChanged(nameof(IsLedgerListVisible));
        this.OnPropertyChanged(nameof(IsLedgerDetailVisible));
    }

    partial void OnIsPurchasesTabChanged(bool value) => this.NotifyCommands();
}
