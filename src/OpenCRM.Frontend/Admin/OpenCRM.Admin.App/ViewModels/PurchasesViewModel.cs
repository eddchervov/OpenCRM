using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Очередь покупок и ручной контроль чеков НПД. </summary>
public partial class PurchasesViewModel(AdminSessionClient sessionClient) : CommunicationsViewModel(sessionClient)
{
    private string _appliedSearch = string.Empty;
    private string _appliedBuyer = string.Empty;
    private string _appliedReceipt = string.Empty;
    private string _appliedQueue = string.Empty;
    private string _appliedPeriod = string.Empty;
    private string _confirmationAction = string.Empty;
    private AdminBillingPurchase? _pendingPurchase;
    private string _pendingReason = string.Empty;
    private bool _resetting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private IReadOnlyList<PurchasesRow> _rows = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenDetailCommand))]
    private PurchasesRow? _selectedRow;

    [ObservableProperty]
    private PurchasesRow? _detail;

    [ObservableProperty]
    private bool _isDetailOpen;

    [ObservableProperty]
    private string _receiptUrl = string.Empty;

    [ObservableProperty]
    private string _receiptComment = string.Empty;

    [ObservableProperty]
    private string _changeReason = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _confirmationMessage = string.Empty;

    [ObservableProperty]
    private bool _isConfirmationOpen;

    [ObservableProperty]
    private CommunicationOption _selectedBuyer = new("", "Все покупатели");

    [ObservableProperty]
    private CommunicationOption _selectedReceipt = new("", "Все чеки");

    [ObservableProperty]
    private CommunicationOption _selectedQueue = new("", "Вся очередь");

    [ObservableProperty]
    private CommunicationOption _selectedPeriod = new("", "Весь период");

    public IReadOnlyList<CommunicationOption> BuyerOptions { get; } = [new("", "Все покупатели"), new("individual", "Физические лица"), new("business", "Организации / ИП"), new("unknown", "Тип не указан")];
    public IReadOnlyList<CommunicationOption> ReceiptOptions { get; } = [new("", "Все чеки"), new("pending", "Без отметки"), new("issued", "Выданные")];
    public IReadOnlyList<CommunicationOption> QueueOptions { get; } = [new("", "Вся очередь"), new("attention", "Требуют внимания"), new("overdue", "Без отметки >24 ч"), new("failed", "Ошибка отправки"), new("queued", "В очереди отправки"), new("sent", "Отправленные"), new("refunded", "С возвратом")];
    public IReadOnlyList<CommunicationOption> PeriodOptions { get; } = [new("", "Весь период"), new("7", "За 7 дней"), new("30", "За 30 дней"), new("90", "За 90 дней")];
    public bool CanManage => this.SessionClient.CurrentProfile is { } profile && (profile.IsSystem || profile.Permissions.Contains("Admin.Billing.Manage", StringComparer.Ordinal));
    public bool CanUpload => this.CanEdit();
    public bool CanDownload => this.CanLoad() && this.HasFile && !this.IsConfirmationOpen;
    public bool HasFile => !string.IsNullOrWhiteSpace(this.Detail?.Purchase.NpdReceiptFileName) || !string.IsNullOrWhiteSpace(this.Detail?.Purchase.NpdReceiptFileUrl);
    public bool IsEmpty => !this.IsBusy && this.Rows.Count == 0 && string.IsNullOrWhiteSpace(this.ErrorMessage);

    public void RefreshAccess()
    {
        if (!this.CanManage)
        {
            this.CancelConfirmation();
        }

        this.NotifyCommands();
    }

    protected override async Task LoadPageAsync(CancellationToken cancellationToken)
    {
        var purchases = await this.SessionClient.GetBillingPurchasesAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var allRows = purchases.Select(purchase => new PurchasesRow(purchase)).ToArray();
        var filtered = allRows.Where(this.MatchesFilters)
            .OrderBy(row => row.IsRefunded).ThenBy(row => row.Purchase.NpdReceiptIssued)
            .ThenByDescending(row => row.IsOverdue).ThenByDescending(row => row.Purchase.NpdReceiptDeliveryStatus == "failed")
            .ThenBy(row => row.Purchase.PaidAt).ThenBy(row => row.Id).ToArray();

        this.TotalCount = filtered.Length;
        this.Page = Math.Clamp(this.Page, 1, Math.Max(1, (filtered.Length + this.PageSize - 1) / this.PageSize));
        this.Rows = filtered.Skip((this.Page - 1) * this.PageSize).Take(this.PageSize).ToArray();
        this.SelectedRow = null;
        this.Summary = $"Покупок: {allRows.Length} · Оплачено: {allRows.Sum(row => row.Purchase.Amount):N2} ₽ · Возвраты: {allRows.Sum(row => row.Refund):N2} ₽ · Итого: {allRows.Sum(row => row.Purchase.Amount - row.Refund):N2} ₽ · Требуют внимания: {allRows.Count(row => row.NeedsAttention)}";
        if (this.IsDetailOpen && this.Detail is { } detail)
        {
            var updated = allRows.FirstOrDefault(row => row.Id == detail.Id);
            if (updated == null)
            {
                this.IsDetailOpen = false;
                this.Detail = null;
            }
            else
            {
                this.SetDetail(updated);
            }
        }

        this.NotifyCommands();
    }

    private bool MatchesFilters(PurchasesRow row)
    {
        var purchase = row.Purchase;
        var matchesSearch = string.IsNullOrEmpty(this._appliedSearch) || new[] { row.Id.ToString(), purchase.User, purchase.UserEmail, purchase.UserPhone, purchase.BuyerName, purchase.BuyerInn, purchase.PlanName }
            .Any(value => value?.Contains(this._appliedSearch, StringComparison.OrdinalIgnoreCase) == true);
        var matchesQueue = this._appliedQueue switch
        {
            "attention" => row.NeedsAttention, "overdue" => row.IsOverdue,
            "refunded" => row.Refund > 0 || row.IsRefunded, "sent" => purchase.NpdReceiptDeliveryStatus is "sent" or "delivered",
            "failed" or "queued" => purchase.NpdReceiptDeliveryStatus == this._appliedQueue, _ => true
        };
        var matchesReceipt = this._appliedReceipt switch { "pending" => !purchase.NpdReceiptIssued, "issued" => purchase.NpdReceiptIssued, _ => true };
        var matchesPeriod = !int.TryParse(this._appliedPeriod, out var days) || purchase.PaidAt.ToUniversalTime() >= DateTime.UtcNow.AddDays(-days);

        return matchesSearch && matchesQueue && matchesReceipt && matchesPeriod
            && (string.IsNullOrEmpty(this._appliedBuyer) || purchase.BuyerType == this._appliedBuyer);
    }

    protected override void ApplyFilters()
    {
        this._appliedSearch = this.Search.Trim();
        this._appliedBuyer = this.SelectedBuyer.Code;
        this._appliedReceipt = this.SelectedReceipt.Code;
        this._appliedQueue = this.SelectedQueue.Code;
        this._appliedPeriod = this.SelectedPeriod.Code;
    }

    protected override void ResetFilters()
    {
        this._resetting = true;
        this.SelectedBuyer = this.BuyerOptions[0];
        this.SelectedReceipt = this.ReceiptOptions[0];
        this.SelectedQueue = this.QueueOptions[0];
        this.SelectedPeriod = this.PeriodOptions[0];
        this._resetting = false;
    }

    partial void OnSelectedBuyerChanged(CommunicationOption value) => this.FilterSelectionChanged();
    partial void OnSelectedReceiptChanged(CommunicationOption value) => this.FilterSelectionChanged();
    partial void OnSelectedQueueChanged(CommunicationOption value) => this.FilterSelectionChanged();
    partial void OnSelectedPeriodChanged(CommunicationOption value) => this.FilterSelectionChanged();

    private void FilterSelectionChanged()
    {
        if (!this._resetting && this.ApplyFiltersCommand.CanExecute(null))
        {
            this.ApplyFiltersCommand.Execute(null);
        }
    }

    protected override void ClearData()
    {
        this.Rows = [];
        this.SelectedRow = null;
        this.Detail = null;
        this.IsDetailOpen = false;
        this.ReceiptUrl = string.Empty;
        this.ReceiptComment = string.Empty;
        this.ChangeReason = string.Empty;
        this.SuccessMessage = string.Empty;
        this.Summary = string.Empty;
        this.CancelConfirmation();
    }

    protected override void NotifyCommands()
    {
        base.NotifyCommands();
        this.OpenDetailCommand.NotifyCanExecuteChanged();
        this.CloseDetailCommand.NotifyCanExecuteChanged();
        this.SaveDetailsCommand.NotifyCanExecuteChanged();
        this.RequestToggleIssuedCommand.NotifyCanExecuteChanged();
        this.RequestDeleteFileCommand.NotifyCanExecuteChanged();
        this.RequestSendCommand.NotifyCanExecuteChanged();
        this.ConfirmCommand.NotifyCanExecuteChanged();
        this.CancelConfirmationCommand.NotifyCanExecuteChanged();
        this.OnPropertyChanged(nameof(CanManage));
        this.OnPropertyChanged(nameof(CanUpload));
        this.OnPropertyChanged(nameof(CanDownload));
        this.OnPropertyChanged(nameof(HasFile));
        this.OnPropertyChanged(nameof(IsEmpty));
    }

    private bool CanOpen() => this.CanLoad() && this.SelectedRow != null;
    private bool CanEdit() => this.CanLoad() && this.CanManage && this.Detail != null && !this.IsConfirmationOpen;
    private bool CanToggle() => this.CanEdit() && !string.IsNullOrWhiteSpace(this.ChangeReason) && this.ChangeReason.Trim().Length <= 500;
    private bool CanDelete() => this.CanEdit() && this.HasFile;
    private bool CanSend() => this.CanEdit() && this.Detail is { } row && !row.IsRefunded
        && row.Purchase.NpdReceiptDeliveryStatus != "queued" && !string.IsNullOrWhiteSpace(row.Purchase.UserEmail)
        && (this.HasFile || !string.IsNullOrWhiteSpace(row.Purchase.NpdReceiptUrl));
    private bool CanConfirm() => this.CanLoad() && this.CanManage && this.Detail != null && this.IsConfirmationOpen;

    partial void OnChangeReasonChanged(string value) => this.RequestToggleIssuedCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void OpenDetail()
    {
        this.SetDetail(this.SelectedRow!);
        this.IsDetailOpen = true;
        this.SuccessMessage = string.Empty;
        this.ErrorMessage = string.Empty;
        this.NotifyCommands();
    }

    private void SetDetail(PurchasesRow row)
    {
        this.Detail = row;
        this.ReceiptUrl = row.Purchase.NpdReceiptUrl ?? string.Empty;
        this.ReceiptComment = row.Purchase.NpdReceiptComment ?? string.Empty;
        this.ChangeReason = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private void CloseDetail()
    {
        this.IsDetailOpen = false;
        this.CancelConfirmation();
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task SaveDetailsAsync()
    {
        var url = this.ReceiptUrl.Trim();
        if (url.Length > 0 && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo) || !(uri.Host.Equals("nalog.ru", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".nalog.ru", StringComparison.OrdinalIgnoreCase))))
        {
            this.ErrorMessage = "Укажите HTTPS-ссылку на nalog.ru или его поддомен.";

            return Task.CompletedTask;
        }

        if (this.ReceiptComment.Length > 1000)
        {
            this.ErrorMessage = "Комментарий не должен превышать 1000 символов.";

            return Task.CompletedTask;
        }

        return this.MutateAsync(token => this.SessionClient.UpdateNpdReceiptDetailsAsync(this.Detail!.Id, url, this.ReceiptComment.Trim(), token));
    }

    [RelayCommand(CanExecute = nameof(CanToggle))]
    private void RequestToggleIssued() => this.RequestConfirmation("issued", this.Detail!.Purchase.NpdReceiptIssued
        ? "Снять отметку о выдаче чека? Изменение с указанной причиной будет сохранено в журнале."
        : "Отметить чек выданным? Подтвердите, что он создан в «Моём налоге» и передан покупателю. Отметка не создаёт и не отправляет чек.");

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void RequestDeleteFile() => this.RequestConfirmation("delete", "Открепить файл чека? Ранее выданные ссылки на этот файл станут недействительными.");

    [RelayCommand(CanExecute = nameof(CanSend))]
    private void RequestSend() => this.RequestConfirmation("send", $"Поставить чек в очередь отправки на {this.Detail!.Purchase.UserEmail}? Будут отправлены сохранённые на сервере файл или ссылка.");

    private void RequestConfirmation(string action, string message)
    {
        this._confirmationAction = action;
        this._pendingPurchase = this.Detail!.Purchase;
        this._pendingReason = this.ChangeReason.Trim();
        this.ConfirmationMessage = $"Покупка #{this._pendingPurchase.PaymentId} · {this._pendingPurchase.User}\n{message}";
        if (action == "issued")
        {
            this.ConfirmationMessage += $"\nТекущее состояние: {(this._pendingPurchase.NpdReceiptIssued ? "выдан" : "без отметки")}. Причина: {this._pendingReason}";
        }

        this.IsConfirmationOpen = true;
        this.NotifyCommands();
    }

    [RelayCommand]
    private void CancelConfirmation()
    {
        this.IsConfirmationOpen = false;
        this._confirmationAction = string.Empty;
        this._pendingPurchase = null;
        this._pendingReason = string.Empty;
        this.ConfirmationMessage = string.Empty;
        this.NotifyCommands();
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private Task ConfirmAsync()
    {
        var purchase = this._pendingPurchase;
        var reason = this._pendingReason;
        var action = this._confirmationAction;
        this.CancelConfirmation();
        if (purchase == null || this.Detail?.Id != purchase.PaymentId)
        {
            return Task.CompletedTask;
        }

        return action switch
        {
            "issued" when reason.Length is > 0 and <= 500 => this.MutateAsync(token => this.SessionClient.MarkNpdReceiptAsync(purchase.PaymentId, !purchase.NpdReceiptIssued, purchase.NpdReceiptIssued, reason, token)),
            "delete" => this.MutateAsync(token => this.SessionClient.DeleteNpdReceiptFileAsync(purchase.PaymentId, token)),
            "send" => this.MutateAsync(token => this.SessionClient.SendNpdReceiptAsync(purchase.PaymentId, token)),
            _ => Task.CompletedTask
        };
    }

    private Task MutateAsync(Func<CancellationToken, Task<AdminOperationResponse>> action) => this.RunAsync(async token =>
    {
        this.SuccessMessage = string.Empty;
        var result = await action(token);
        token.ThrowIfCancellationRequested();
        this.SuccessMessage = result.Message;
        await this.LoadPageAsync(token);
    });

    public Task UploadFileAsync(Stream stream, string fileName, string contentType)
    {
        if (!this.CanUpload)
        {
            return Task.CompletedTask;
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not (".pdf" or ".png" or ".jpg" or ".jpeg") || contentType is not ("application/pdf" or "image/png" or "image/jpeg"))
        {
            this.ErrorMessage = "Выберите PDF, PNG или JPG размером до 5 МБ.";

            return Task.CompletedTask;
        }

        return this.RunAsync(async token =>
        {
            using var buffer = new MemoryStream();
            var bytes = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(bytes, token)) > 0)
            {
                if (buffer.Length + read > 5_242_880)
                {
                    this.ErrorMessage = "Размер файла не должен превышать 5 МБ.";

                    return;
                }

                await buffer.WriteAsync(bytes.AsMemory(0, read), token);
            }

            token.ThrowIfCancellationRequested();
            if (buffer.Length == 0)
            {
                this.ErrorMessage = "Файл пуст.";

                return;
            }

            buffer.Position = 0;
            var result = await this.SessionClient.UploadNpdReceiptFileAsync(this.Detail!.Id, buffer, fileName, contentType, token);
            token.ThrowIfCancellationRequested();
            this.SuccessMessage = result.Message;
            await this.LoadPageAsync(token);
        });
    }

    public async Task<AdminNpdReceiptFile?> DownloadFileAsync()
    {
        if (!this.CanDownload)
        {
            return null;
        }

        AdminNpdReceiptFile? file = null;
        await this.RunAsync(async token =>
        {
            var result = await this.SessionClient.DownloadNpdReceiptFileAsync(this.Detail!.Id, token);
            token.ThrowIfCancellationRequested();
            file = result;
        });

        return file;
    }
}
