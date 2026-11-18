using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.Localization;

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
    private int _summaryPurchaseCount;
    private decimal _summaryPaidAmount;
    private decimal _summaryRefundAmount;
    private decimal _summaryTotalAmount;
    private int _summaryAttentionCount;

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
    private CommunicationOption _selectedBuyer = CommunicationOption.Localized("", "native.billing.vse.pokupateli");

    [ObservableProperty]
    private CommunicationOption _selectedReceipt = CommunicationOption.Localized("", "native.billing.vse.cheki");

    [ObservableProperty]
    private CommunicationOption _selectedQueue = CommunicationOption.Localized("", "native.billing.vsya.ochered");

    [ObservableProperty]
    private CommunicationOption _selectedPeriod = CommunicationOption.Localized("", "native.billing.ves.period");

    public IReadOnlyList<CommunicationOption> BuyerOptions { get; } = [CommunicationOption.Localized("", "native.billing.vse.pokupateli"), CommunicationOption.Localized("individual", "native.billing.fizicheskie.litsa"), CommunicationOption.Localized("business", "native.billing.organizatsii.ip"), CommunicationOption.Localized("unknown", "native.billing.tip.ne.ukazan")];
    public IReadOnlyList<CommunicationOption> ReceiptOptions { get; } = [CommunicationOption.Localized("", "native.billing.vse.cheki"), CommunicationOption.Localized("pending", "native.billing.bez.otmetki"), CommunicationOption.Localized("issued", "native.billing.vydannye")];
    public IReadOnlyList<CommunicationOption> QueueOptions { get; } = [CommunicationOption.Localized("", "native.billing.vsya.ochered"), CommunicationOption.Localized("attention", "native.billing.trebuyut.vnimaniya"), CommunicationOption.Localized("overdue", "native.billing.bez.otmetki.24.ch"), CommunicationOption.Localized("failed", "native.billing.oshibka.otpravki"), CommunicationOption.Localized("queued", "native.billing.v.ocheredi.otpravki"), CommunicationOption.Localized("sent", "native.billing.otpravlennye"), CommunicationOption.Localized("refunded", "native.billing.s.vozvratom")];
    public IReadOnlyList<CommunicationOption> PeriodOptions { get; } = [CommunicationOption.Localized("", "native.billing.ves.period"), CommunicationOption.Localized("7", "native.billing.za.7.dney"), CommunicationOption.Localized("30", "native.billing.za.30.dney"), CommunicationOption.Localized("90", "native.billing.za.90.dney")];
    public bool CanManage => this.SessionClient.CurrentProfile is { } profile && (profile.IsSystem || profile.Permissions.Contains("Admin.Billing.Manage", StringComparer.Ordinal));
    public bool CanUpload => this.CanEdit();
    public bool CanDownload => this.CanLoad() && this.HasFile && !this.IsConfirmationOpen;
    public bool HasFile => !string.IsNullOrWhiteSpace(this.Detail?.Purchase.NpdReceiptFileName) || !string.IsNullOrWhiteSpace(this.Detail?.Purchase.NpdReceiptFileUrl);
    public bool IsEmpty => !this.IsBusy && this.Rows.Count == 0 && string.IsNullOrWhiteSpace(this.ErrorMessage);

    public override void RefreshLocalizedValues()
    {
        base.RefreshLocalizedValues();
        foreach (var option in this.BuyerOptions.Concat(this.ReceiptOptions).Concat(this.QueueOptions).Concat(this.PeriodOptions))
        {
            option.RefreshLocalizedValues();
        }

        foreach (var row in this.Rows)
        {
            row.RefreshLocalizedValues();
        }

        this.Detail?.RefreshLocalizedValues();
        this.UpdateSummary();
        if (this.IsConfirmationOpen && this._pendingPurchase != null)
        {
            this.UpdateConfirmationMessage();
        }
    }

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
        this._summaryPurchaseCount = allRows.Length;
        this._summaryPaidAmount = allRows.Sum(row => row.Purchase.Amount);
        this._summaryRefundAmount = allRows.Sum(row => row.Refund);
        this._summaryTotalAmount = allRows.Sum(row => row.Purchase.Amount - row.Refund);
        this._summaryAttentionCount = allRows.Count(row => row.NeedsAttention);
        this.UpdateSummary();
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
            this.SetLocalizedErrorMessage("native.billing.ukazhite.https.ssylku.na.nalog.ru.ili.ego.poddomen");

            return Task.CompletedTask;
        }

        if (this.ReceiptComment.Length > 1000)
        {
            this.SetLocalizedErrorMessage("native.billing.kommentariy.ne.dolzhen.prevyshat.1000.simvolov");

            return Task.CompletedTask;
        }

        return this.MutateAsync(token => this.SessionClient.UpdateNpdReceiptDetailsAsync(this.Detail!.Id, url, this.ReceiptComment.Trim(), token));
    }

    [RelayCommand(CanExecute = nameof(CanToggle))]
    private void RequestToggleIssued() => this.RequestConfirmation("issued");

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void RequestDeleteFile() => this.RequestConfirmation("delete");

    [RelayCommand(CanExecute = nameof(CanSend))]
    private void RequestSend() => this.RequestConfirmation("send");

    private void RequestConfirmation(string action)
    {
        this._confirmationAction = action;
        this._pendingPurchase = this.Detail!.Purchase;
        this._pendingReason = this.ChangeReason.Trim();
        this.UpdateConfirmationMessage();

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

    private void UpdateConfirmationMessage()
    {
        if (this._pendingPurchase == null)
        {
            this.ConfirmationMessage = string.Empty;

            return;
        }

        var message = this._confirmationAction switch
        {
            "issued" => this._pendingPurchase.NpdReceiptIssued
                ? AdminLocalization.Instance["native.billing.snyat.otmetku.o.vydache.cheka.izmenenie.s.ukazannoy.prichinoy.bu"]
                : AdminLocalization.Instance["native.billing.otmetit.chek.vydannym.podtverdite.chto.on.sozdan.v.moyom.naloge."],
            "delete" => AdminLocalization.Instance["native.billing.otkrepit.fayl.cheka.ranee.vydannye.ssylki.na.etot.fayl.stanut.ne"],
            "send" => AdminLocalization.Instance.Format("native.billing.postavit.chek.v.ochered.otpravki.na.this.detail.purchase.userema", this._pendingPurchase.UserEmail),
            _ => string.Empty
        };
        this.ConfirmationMessage = AdminLocalization.Instance.Format(
            "native.billing.pokupka.this.pendingpurchase.paymentid.this.pendingpurchase.user",
            this._pendingPurchase.PaymentId,
            this._pendingPurchase.User,
            message);
        if (this._confirmationAction == "issued")
        {
            var receiptState = this._pendingPurchase.NpdReceiptIssued
                ? AdminLocalization.Instance["native.billing.vydan"]
                : AdminLocalization.Instance["native.billing.bez.otmetki"];
            this.ConfirmationMessage += AdminLocalization.Instance.Format(
                "native.billing.ntekushchee.sostoyanie.this.pendingpurchase.npdreceiptissued.pri",
                receiptState,
                this._pendingReason);
        }
    }

    private void UpdateSummary() => this.Summary = AdminLocalization.Instance.Format(
        "native.billing.pokupok.allrows.length.oplacheno.allrows.sum.row.row.purchase.am",
        this._summaryPurchaseCount,
        this._summaryPaidAmount,
        this._summaryRefundAmount,
        this._summaryTotalAmount,
        this._summaryAttentionCount);

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
            this.SetLocalizedErrorMessage("native.billing.vyberite.pdf.png.ili.jpg.razmerom.do.5.mb");

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
                    this.SetLocalizedErrorMessage("native.billing.razmer.fayla.ne.dolzhen.prevyshat.5.mb");

                    return;
                }

                await buffer.WriteAsync(bytes.AsMemory(0, read), token);
            }

            token.ThrowIfCancellationRequested();
            if (buffer.Length == 0)
            {
                this.SetLocalizedErrorMessage("native.billing.fayl.pust");

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
