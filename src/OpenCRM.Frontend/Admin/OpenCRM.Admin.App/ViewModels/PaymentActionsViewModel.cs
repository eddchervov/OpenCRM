using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Подтверждённые административные действия над выбранным платежом. </summary>
public partial class PaymentActionsViewModel(AdminSessionClient sessionClient, PendingPaymentActionStore? pendingActionStore = null) : ObservableObject
{
    private readonly AdminSessionClient _sessionClient = sessionClient;
    private readonly PendingPaymentActionStore _pendingActionStore = pendingActionStore ?? new();
    private bool _hasDraftLoadError;
    private AdminBillingPayment? _payment;
    private bool _isActive;
    private int _version;
    private string? _pendingAction;
    private string? _expectedStatus;
    private string _pendingStatus = string.Empty;
    private string _pendingReason = string.Empty;
    private AdminBillingPaymentServiceActionRequest? _pendingServiceRequest;
    private int? _pendingServicePaymentId;
    private string? _pendingServiceScope;
    private bool _serviceRequestMayHaveReachedServer;
    private AdminBillingPaymentServiceActions? _serviceActions;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RequestServiceActionCommand))]
    private decimal? _refundAmount;

    [ObservableProperty]
    private string _checkHint = string.Empty;

    [ObservableProperty]
    private string _refundHint = string.Empty;

    [ObservableProperty]
    private string _retryAccessHint = string.Empty;

    [ObservableProperty]
    private string _resolveReviewHint = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<AdminBillingPaymentServiceActionOperation> _serviceOperations = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RequestStatusChangeCommand))]
    [NotifyCanExecuteChangedFor(nameof(RequestServiceActionCommand))]
    private string _reason = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RequestStatusChangeCommand))]
    private CommunicationOption _selectedStatus = new("completed", "Оплачено");

    [ObservableProperty]
    private bool _isConfirmationOpen;

    [ObservableProperty]
    private string _confirmationText = string.Empty;

    public IReadOnlyList<CommunicationOption> StatusOptions { get; } =
    [
        new("pending", "Ожидание оплаты"), new("completed", "Оплачено"),
        new("failed", "Ошибка"), new("canceled", "Отменено"), new("refunded", "Возврат")
    ];

    public bool IsVisible => this._payment != null
        && (this.CanManage || this.CanChangeStatus || this.ErrorMessage.Length > 0 || this.SuccessMessage.Length > 0);

    public bool CanManage => this._sessionClient.CurrentProfile is { } profile
        && (profile.IsSystem || profile.Permissions.Contains("Admin.Billing.Manage"));

    public bool CanChangeStatus => this._sessionClient.CurrentProfile?.IsSystem == true;

    public bool HasExternalId => this._payment?.HasExternalId == true;

    public bool IsServicePayment => this._payment?.PaymentProviderCode == "payments-service";

    public bool CanCheck => this.IsServicePayment && this.CanManage && this._serviceActions?.CanCheck == true;

    public bool CanRefund => this.IsServicePayment && this.CanManage && this._serviceActions?.CanRefund == true;

    public bool CanRetryAccess => this.IsServicePayment && this.CanManage && this._serviceActions?.CanRetryAccess == true;

    public bool CanResolveReview => this.IsServicePayment && this.CanManage && this._serviceActions?.CanResolveReview == true;

    public decimal RefundableAmount => this._serviceActions?.RefundableAmount ?? 0;

    public bool HasUnresolvedServiceRequest => this._pendingServiceRequest != null;

    public bool CanRetryUnresolvedServiceRequest => this._pendingServiceRequest != null
        && this._pendingServicePaymentId == this._payment?.Id && this.IsServicePayment && this.CanManage && !this.IsBusy
        && this._pendingServiceScope == this._sessionClient.PendingActionScope;

    public string PendingServiceRequestHint => this._pendingServiceRequest == null
        ? string.Empty
        : $"Команда по платежу № {this._pendingServicePaymentId} ещё не получила окончательный ответ. Повтор отправит тот же запрос с прежним номером операции.";

    public event Action? SessionExpired;

    public event Func<Task>? AccessDenied;

    public event Func<CancellationToken, Task>? PaymentChanged;

    /// <summary> Обновляет платёж, сохраняя введённую причину при обновлении той же карточки. </summary>
    public void SetPayment(AdminBillingPayment? payment)
    {
        if (this._payment?.Id != payment?.Id)
        {
            this.Stop(true);
            this.SelectedStatus = this.StatusOptions.FirstOrDefault(option => option.Code == payment?.PaymentStatus)
                ?? this.StatusOptions[0];
        }

        this._payment = payment;
        this._isActive = payment != null;
        this.RefreshAccess();
    }

    public void RefreshAccess()
    {
        this.OnPropertyChanged(nameof(IsVisible));
        this.OnPropertyChanged(nameof(CanManage));
        this.OnPropertyChanged(nameof(CanChangeStatus));
        this.OnPropertyChanged(nameof(HasExternalId));
        this.OnPropertyChanged(nameof(IsServicePayment));
        this.OnPropertyChanged(nameof(CanCheck));
        this.OnPropertyChanged(nameof(CanRefund));
        this.OnPropertyChanged(nameof(CanRetryAccess));
        this.OnPropertyChanged(nameof(CanResolveReview));
        this.OnPropertyChanged(nameof(RefundableAmount));
        this.OnPropertyChanged(nameof(HasUnresolvedServiceRequest));
        this.OnPropertyChanged(nameof(CanRetryUnresolvedServiceRequest));
        this.OnPropertyChanged(nameof(PendingServiceRequestHint));
        this.RequestSyncCommand.NotifyCanExecuteChanged();
        this.RequestStatusChangeCommand.NotifyCanExecuteChanged();
        this.RequestServiceActionCommand.NotifyCanExecuteChanged();
        this.RetryUnresolvedServiceActionCommand.NotifyCanExecuteChanged();
        this.ConfirmCommand.NotifyCanExecuteChanged();
    }

    public void Stop(bool clear = false)
    {
        this._version++;
        this._isActive = false;
        this.ConfirmCommand.Cancel();
        this.IsConfirmationOpen = false;
        this._pendingAction = null;
        if (!this._serviceRequestMayHaveReachedServer)
        {
            this._pendingServiceRequest = null;
            this._pendingServicePaymentId = null;
        }
        if (clear)
        {
            this._payment = null;
            this._serviceActions = null;
            this.ServiceOperations = [];
            this.Reason = string.Empty;
            this.ErrorMessage = string.Empty;
            this.SuccessMessage = string.Empty;
        }

        this.IsBusy = false;
        this.RefreshAccess();
    }

    public async Task DeactivateAsync(bool clear = false)
    {
        this.Stop(clear);
        if (this.ConfirmCommand.ExecutionTask is { } operation)
        {
            await operation;
        }
    }

    private bool CanSync() => this._isActive && this._payment != null && !this.IsServicePayment
        && !this.IsBusy && this.CanManage && this.HasExternalId;

    private bool CanMark() => this._isActive && this._payment != null && !this.IsServicePayment && !this.IsBusy && this.CanChangeStatus
        && this.Reason.Trim().Length is > 0 and <= 500 && this.StatusOptions.Contains(this.SelectedStatus);

    private bool CanConfirm() => this.IsConfirmationOpen
        && (this._pendingAction == "sync" ? this.CanSync()
            : this._pendingAction == "status" ? this.CanMark()
            : this._pendingAction == "service" && this.CanExecutePendingServiceAction());

    private bool CanExecutePendingServiceAction() => this._pendingServiceRequest != null && this.CanManage
        && this._pendingServicePaymentId == this._payment?.Id && this.IsServicePayment && !this.IsBusy
        && this._pendingServiceScope == this._sessionClient.PendingActionScope;

    [RelayCommand(CanExecute = nameof(CanSync))]
    private void RequestSync()
    {
        this._pendingAction = "sync";
        this.ConfirmationText = $"Сверить платёж № {this._payment!.Id} с YooKassa? Сверка может обновить статус и сведения об оплате.";
        this.IsConfirmationOpen = true;
        this.ConfirmCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanMark))]
    private void RequestStatusChange()
    {
        this._pendingAction = "status";
        this._expectedStatus = this._payment!.PaymentStatus;
        this._pendingStatus = this.SelectedStatus.Code;
        this._pendingReason = this.Reason.Trim();
        this.ConfirmationText = $"Платёж № {this._payment.Id}: установить статус «{this.SelectedStatus.Name}»? "
            + $"Причина: {this._pendingReason}. Изменяется локальная запись; возврат денег этим действием не выполняется.";
        this.IsConfirmationOpen = true;
        this.ConfirmCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRequestServiceAction))]
    private void RequestServiceAction(string action)
    {
        var label = action switch
        {
            "check" => "проверить платёж в ЮKassa",
            "refund" => $"вернуть {this.RefundAmount:N2} ₽",
            "retry-access" => "повторить выдачу подтверждённого доступа",
            "resolve-review" => "сохранить решение по разбору",
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        this._pendingAction = "service";
        this._pendingServicePaymentId = this._payment!.Id;
        this._pendingServiceScope = this._sessionClient.PendingActionScope;
        this._pendingServiceRequest = new AdminBillingPaymentServiceActionRequest(
            Guid.NewGuid(), action, this.Reason.Trim(), action == "refund" ? this.RefundAmount : null,
            this._serviceActions!.StateToken);
        this._serviceRequestMayHaveReachedServer = false;
        this.ConfirmationText = $"Подтвердите действие: {label}. Причина: {this.Reason.Trim()}."
            + (action == "refund" ? " Выданный срок доступа автоматически не изменится." : string.Empty);
        this.IsConfirmationOpen = true;
        this.ConfirmCommand.NotifyCanExecuteChanged();
    }

    private bool CanRequestServiceAction(string action)
    {
        if (!this._isActive || !this.IsServicePayment || this.IsBusy || !this.CanManage
            || this._pendingServiceRequest != null || this._hasDraftLoadError
            || this.Reason.Trim().Length is 0 or > 500 || this._serviceActions == null)
        {
            return false;
        }

        return action switch
        {
            "check" => this.CanCheck,
            "refund" => this.CanRefund && this.RefundAmount is > 0 and var amount
                && decimal.Round(amount, 2) == amount && amount <= this._serviceActions.RefundableAmount,
            "retry-access" => this.CanRetryAccess,
            "resolve-review" => this.CanResolveReview,
            _ => false
        };
    }

    [RelayCommand(CanExecute = nameof(CanRetryUnresolved))]
    private void RetryUnresolvedServiceAction()
    {
        if (!this.CanRetryUnresolved())
        {
            return;
        }

        this._pendingAction = "service";
        this.ConfirmationText = $"Повторить операцию {this._pendingServiceRequest!.OperationId:D}? Будет отправлен тот же запрос, новая операция не создаётся.";
        this.IsConfirmationOpen = true;
        this.ConfirmCommand.NotifyCanExecuteChanged();
    }

    private bool CanRetryUnresolved() => this.CanRetryUnresolvedServiceRequest;

    public async Task LoadServiceActionsAsync(CancellationToken cancellationToken = default)
    {
        var payment = this._payment;
        var version = this._version;
        if (payment == null || payment.PaymentProviderCode != "payments-service")
        {
            return;
        }

        var currentScope = this._sessionClient.PendingActionScope;
        try
        {
            if (this._pendingServiceScope != null && this._pendingServiceScope != currentScope)
            {
                this._pendingServiceRequest = null;
                this._pendingServicePaymentId = null;
                this._pendingServiceScope = null;
                this._serviceRequestMayHaveReachedServer = false;
                this.IsConfirmationOpen = false;
                this._pendingAction = null;
                this.OnPropertyChanged(nameof(HasUnresolvedServiceRequest));
                this.OnPropertyChanged(nameof(PendingServiceRequestHint));
            }

            var savedDraft = await this._pendingActionStore.LoadAsync(currentScope, cancellationToken);
            if (!this.IsCurrent(payment.Id, version, cancellationToken)
                || currentScope != this._sessionClient.PendingActionScope)
            {
                return;
            }

            this._hasDraftLoadError = false;
            if (this._pendingServiceRequest == null && savedDraft != null)
            {
                this._pendingServiceRequest = savedDraft.Request;
                this._pendingServicePaymentId = savedDraft.PaymentId;
                this._pendingServiceScope = currentScope;
                this._serviceRequestMayHaveReachedServer = true;
                this.OnPropertyChanged(nameof(HasUnresolvedServiceRequest));
                this.OnPropertyChanged(nameof(CanRetryUnresolvedServiceRequest));
                this.OnPropertyChanged(nameof(PendingServiceRequestHint));
            }

            var serviceActions = await this._sessionClient.GetBillingPaymentServiceActionsAsync(payment.Id, cancellationToken);
            if (!this.IsCurrent(payment.Id, version, cancellationToken)
                || currentScope != this._sessionClient.PendingActionScope)
            {
                return;
            }

            var stateChanged = this._serviceActions != null && this._serviceActions.StateToken != serviceActions.StateToken;
            this._serviceActions = serviceActions;
            if (stateChanged && this._pendingAction == "service" && !this._serviceRequestMayHaveReachedServer)
            {
                this._pendingServiceRequest = null;
                this._pendingServicePaymentId = null;
                this._pendingServiceScope = null;
                this.IsConfirmationOpen = false;
                this._pendingAction = null;
                this.ErrorMessage = "Состояние платежа изменилось. Проверьте обновлённые сведения и подтвердите действие заново.";
                this.OnPropertyChanged(nameof(HasUnresolvedServiceRequest));
                this.OnPropertyChanged(nameof(CanRetryUnresolvedServiceRequest));
                this.OnPropertyChanged(nameof(PendingServiceRequestHint));
            }
            this.CheckHint = serviceActions.CheckHint;
            this.RefundHint = serviceActions.RefundHint;
            this.RetryAccessHint = serviceActions.RetryAccessHint;
            this.ResolveReviewHint = serviceActions.ResolveReviewHint;
            this.ServiceOperations = serviceActions.Operations;
            this.RefundAmount = serviceActions.RefundableAmount;
            this.OnPropertyChanged(nameof(RefundableAmount));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            if (this.IsCurrent(payment.Id, version, cancellationToken)
                && currentScope == this._sessionClient.PendingActionScope)
            {
                this._hasDraftLoadError = true;
                this.ErrorMessage = "Не удалось прочитать сохранённую операцию. Новые действия заблокированы; проверьте журнал и повторите загрузку.";
                this.RefreshAccess();
            }

            return;
        }
        catch (ApiException exception)
        {
            if (version == this._version && this._payment?.Id == payment.Id
                && currentScope == this._sessionClient.PendingActionScope)
            {
                this.ErrorMessage = exception.Message;
            }

            return;
        }

        if (version == this._version && this._payment?.Id == payment.Id
            && currentScope == this._sessionClient.PendingActionScope)
        {
            this.RefreshAccess();
            if (this._pendingServiceRequest is { } pendingRequest && this._pendingServicePaymentId == payment.Id)
            {
                var operation = this._serviceActions.Operations.SingleOrDefault(item => item.Id == pendingRequest.OperationId);
                if (operation != null)
                {
                    if (operation.Action == pendingRequest.Action && operation.Amount == pendingRequest.Amount
                        && operation.Reason == pendingRequest.Reason)
                    {
                        this._pendingServiceRequest = null;
                        this._pendingServicePaymentId = null;
                        var operationScope = this._pendingServiceScope!;
                        this._pendingServiceScope = null;
                        this._serviceRequestMayHaveReachedServer = false;
                        await this._pendingActionStore.DeleteAsync(operationScope);
                        this.OnPropertyChanged(nameof(HasUnresolvedServiceRequest));
                        this.OnPropertyChanged(nameof(CanRetryUnresolvedServiceRequest));
                        this.OnPropertyChanged(nameof(PendingServiceRequestHint));
                        this.RefreshAccess();
                    }
                    else
                    {
                        this.ErrorMessage = "Номер операции найден в журнале с другими данными. Не создавайте новую команду; передайте её номер ответственному администратору.";
                    }
                }
            }
        }
    }

    [RelayCommand]
    private void CancelConfirmation()
    {
        this.IsConfirmationOpen = false;
        this._pendingAction = null;
        if (!this._serviceRequestMayHaveReachedServer)
        {
            this._pendingServiceRequest = null;
            this._pendingServicePaymentId = null;
            this._pendingServiceScope = null;
            this.OnPropertyChanged(nameof(HasUnresolvedServiceRequest));
            this.OnPropertyChanged(nameof(CanRetryUnresolvedServiceRequest));
            this.OnPropertyChanged(nameof(PendingServiceRequestHint));
        }
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync(CancellationToken cancellationToken)
    {
        var paymentId = this._payment!.Id;
        var version = this._version;
        var action = this._pendingAction;
        var serviceRequest = this._pendingServiceRequest;
        var serviceScope = this._pendingServiceScope;
        var requestMayHaveReachedServer = false;
        this.IsConfirmationOpen = false;
        this.IsBusy = true;
        this.ErrorMessage = string.Empty;
        this.SuccessMessage = string.Empty;
        try
        {
            if (action == "service")
            {
                if (serviceRequest == null || serviceScope != this._sessionClient.PendingActionScope)
                {
                    this.ErrorMessage = "Текущий администратор или адрес API изменился. Откройте платёж заново для своей учётной записи.";
                    return;
                }

                await this._pendingActionStore.SaveAsync(serviceScope!,
                    new PendingPaymentActionDraft(paymentId, serviceRequest), cancellationToken);
                if (!this.IsCurrent(paymentId, version, cancellationToken) || serviceScope != this._sessionClient.PendingActionScope)
                {
                    return;
                }
                this._serviceRequestMayHaveReachedServer = true;
                requestMayHaveReachedServer = true;
                var operation = await this._sessionClient.ExecuteBillingPaymentServiceActionAsync(
                    paymentId, serviceRequest, cancellationToken);
                if (!this.IsCurrent(paymentId, version, cancellationToken) || serviceScope != this._sessionClient.PendingActionScope)
                {
                    return;
                }

                if (operation.Id != serviceRequest.OperationId
                    || operation.Action != serviceRequest.Action
                    || operation.Amount != serviceRequest.Amount
                    || operation.Reason != serviceRequest.Reason)
                {
                    this.ErrorMessage = "Ответ сервера не совпадает с отправленной командой. Команда сохранена; проверьте журнал перед повтором.";
                    this.IsConfirmationOpen = true;
                    this._pendingAction = "service";
                    this.ConfirmCommand.NotifyCanExecuteChanged();
                    await this.LoadServiceActionsAsync(cancellationToken);

                    return;
                }

                await this._pendingActionStore.DeleteAsync(serviceScope!);
                this._pendingServiceRequest = null;
                this._pendingServicePaymentId = null;
                this._pendingServiceScope = null;
                this._serviceRequestMayHaveReachedServer = false;
                if (!this.IsCurrent(paymentId, version, cancellationToken) || serviceScope != this._sessionClient.PendingActionScope)
                {
                    return;
                }

                this.OnPropertyChanged(nameof(HasUnresolvedServiceRequest));
                this.OnPropertyChanged(nameof(CanRetryUnresolvedServiceRequest));
                this.OnPropertyChanged(nameof(PendingServiceRequestHint));
                this.SuccessMessage = operation.Message;
                await this.LoadServiceActionsAsync(cancellationToken);
                if (this.PaymentChanged != null)
                {
                    await this.PaymentChanged.Invoke(cancellationToken);
                }

                return;
            }

            var result = action == "sync"
                ? await this._sessionClient.SyncBillingPaymentAsync(paymentId, cancellationToken)
                : await this._sessionClient.MarkBillingPaymentStatusAsync(
                    paymentId, this._pendingStatus, this._expectedStatus, this._pendingReason, cancellationToken);

            if (!this.IsCurrent(paymentId, version, cancellationToken))
            {
                return;
            }

            this.SuccessMessage = result.Message;
            if (this.PaymentChanged != null)
            {
                await this.PaymentChanged.Invoke(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Закрытие карточки отменяет ожидание и не изменяет новый экран.
        }
        catch (ApiException exception)
        {
            if (!this.IsCurrent(paymentId, version, cancellationToken)
                || action == "service" && serviceScope != this._sessionClient.PendingActionScope)
            {
                return;
            }

            this.ErrorMessage = exception.Message;
            var isDefinitiveServiceRejection = exception.ErrorCode is "invalid_request" or "state_changed"
                || exception.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound;
            if (this._pendingServiceRequest != null && isDefinitiveServiceRejection)
            {
                this._pendingServiceRequest = null;
                this._pendingServicePaymentId = null;
                var operationScope = this._pendingServiceScope!;
                this._pendingServiceScope = null;
                this._serviceRequestMayHaveReachedServer = false;
                await this._pendingActionStore.DeleteAsync(operationScope);
                this.OnPropertyChanged(nameof(HasUnresolvedServiceRequest));
                this.OnPropertyChanged(nameof(CanRetryUnresolvedServiceRequest));
                this.OnPropertyChanged(nameof(PendingServiceRequestHint));
                await this.LoadServiceActionsAsync(cancellationToken);
            }

            if (this._pendingServiceRequest != null && !isDefinitiveServiceRejection)
            {
                this.ErrorMessage = exception.ErrorCode == "operation_id_conflict"
                    ? "Сервер сообщил о конфликте номера операции. Новая команда не создана. Сверьте журнал и обратитесь к ответственному администратору."
                    : "Ответ сервера не подтверждает результат. Повторите отправку той же операции или обновите журнал.";
                this.IsConfirmationOpen = true;
                this._pendingAction = "service";
            }

            if (exception.StatusCode == HttpStatusCode.Unauthorized || !this._sessionClient.HasSession)
            {
                this.SessionExpired?.Invoke();
            }
            else if (exception.StatusCode == HttpStatusCode.Forbidden && this.AccessDenied != null)
            {
                await this.AccessDenied.Invoke();
            }
            else if (exception.StatusCode == HttpStatusCode.BadRequest && this.PaymentChanged != null)
            {
                await this.PaymentChanged.Invoke(cancellationToken);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (this.IsCurrent(paymentId, version, cancellationToken)
                && serviceScope == this._sessionClient.PendingActionScope)
            {
                this.ErrorMessage = requestMayHaveReachedServer
                    ? "Не удалось сохранить локальный результат. Запрос мог быть принят; проверьте журнал перед новым действием."
                    : "Не удалось сохранить номер операции. Запрос не отправлен; проверьте доступ к локальному хранилищу.";
                this.IsConfirmationOpen = this._pendingServiceRequest != null;
                this._pendingAction = this._pendingServiceRequest == null ? null : "service";
            }
        }
        catch (HttpRequestException)
        {
            if (this.IsCurrent(paymentId, version, cancellationToken)
                && (action != "service" || serviceScope == this._sessionClient.PendingActionScope))
            {
                this.ErrorMessage = this._pendingServiceRequest == null
                    ? "Не удалось связаться с сервером. Обновите платёж перед повтором операции."
                    : "Не удалось подтвердить результат. Повторите отправку прежней операции с тем же номером.";
                this.IsConfirmationOpen = this._pendingServiceRequest != null;
                this._pendingAction = this._pendingServiceRequest == null ? null : "service";
            }
        }
        catch (OperationCanceledException)
        {
            if (this.IsCurrent(paymentId, version, cancellationToken)
                && (action != "service" || serviceScope == this._sessionClient.PendingActionScope))
            {
                this.ErrorMessage = this._pendingServiceRequest == null
                    ? "Сервер не ответил вовремя. Обновите платёж перед повтором операции."
                    : "Сервер не ответил. Повторите отправку прежней операции с тем же номером.";
                this.IsConfirmationOpen = this._pendingServiceRequest != null;
                this._pendingAction = this._pendingServiceRequest == null ? null : "service";
            }
        }
        finally
        {
            if (version == this._version)
            {
                this.IsBusy = false;
                if (this._pendingServiceRequest == null)
                {
                    this._pendingAction = null;
                }
            }
        }
    }

    private bool IsCurrent(int paymentId, int version, CancellationToken cancellationToken) =>
        this._isActive && this._payment?.Id == paymentId && version == this._version && !cancellationToken.IsCancellationRequested;

    partial void OnIsBusyChanged(bool value) => this.RefreshAccess();
}
