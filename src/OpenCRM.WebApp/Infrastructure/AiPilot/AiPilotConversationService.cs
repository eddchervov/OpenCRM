using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using OpenCRM.Modules.Vehicles.Contracts;
using Microsoft.Extensions.Caching.Memory;

namespace OpenCRM.WebApp.Infrastructure.AiPilot;

public sealed record AiPilotConversationMessage(string Role, string Content);

public sealed record AiPilotChatDraft(
    string DraftToken,
    AiPilotCommand Command,
    string Summary,
    IReadOnlyList<string> Warnings,
    decimal? Cost);

public sealed record AiPilotChatResponse(
    string ConversationId,
    string Message,
    bool AwaitingAnswer,
    AiPilotChatDraft? Draft = null,
    decimal? Cost = null,
    bool ContinuesPendingCommand = false);

public sealed record AiPilotConversationSnapshot(
    IReadOnlyList<AiPilotConversationMessage> Messages,
    int UserTurns,
    AiPilotChatDraft? PendingDraft,
    AiPilotCommand? PendingCommand,
    string? RequestedSlot);

public sealed class AiPilotConversationService(IMemoryCache cache, RouterAiPilotClient router, IVehicleRegistrationPlatePolicy? registrationPlates = null)
{
    public void Restore(string userId, Guid organizationId, Guid conversationId, AiPilotConversationSnapshot snapshot)
    {
        var state = new ConversationState
        {
            UserTurns = snapshot.UserTurns,
            PendingDraft = snapshot.PendingDraft,
            PendingCommand = snapshot.PendingCommand,
            RequestedSlot = snapshot.RequestedSlot
        };
        foreach (var message in snapshot.Messages)
        {
            if (!this.TryAddMessage(state, message.Role, message.Content))
            {
                throw new InvalidOperationException("История разговора превышает допустимый размер. Начните новый разговор.");
            }
        }

        this.Save(GetKey(userId, organizationId, conversationId), state);
    }

    public AiPilotConversationSnapshot Export(string userId, Guid organizationId, Guid conversationId)
    {
        var state = this.GetState(GetKey(userId, organizationId, conversationId), false)
            ?? throw new InvalidOperationException("Разговор не найден.");

        return new AiPilotConversationSnapshot(state.History.ToArray(), state.UserTurns, state.PendingDraft, state.PendingCommand, state.RequestedSlot);
    }

    private static readonly object s_stateCreationLock = new();
    private static readonly Regex s_newTaskPrefix = new("^(запиши|создай|удали|перенеси|найди|покажи|отмени|закрой|верни|начни|заверши|прими оплату|добавь в каталог|измени заказ)\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex s_completedVisitPrefix = new("^(?:мы\\s+)?(сделали|помыли|заменили|поменяли|покрасили|починили|выполнили)\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex s_priceReplyPattern = new(
        @"^(?:(?:цена|стоимость|за)\s+)?(?<amount>\d[\d \u00A0]*(?:[,.]\d{1,2})?)\s*(?:руб(?:лей|ля|ль)?\.?|₽)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex s_totalPriceReplyPattern = new(
        @"\b(?:всего|итого|суммарно|общая|общую|общей|общий|общего)\b|\bза\s+(?:все|всё|весь|всю|всех|обе|обоих|обеих|две|три)\b|\bв\s+целом\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<AiPilotChatResponse> ChatAsync(
        string userId,
        Guid organizationId,
        Guid? conversationId,
        string text,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > router.RuntimeOptions.MaximumMessageCharacters)
        {
            throw new InvalidOperationException($"Сообщение должно содержать от 1 до {router.RuntimeOptions.MaximumMessageCharacters} символов.");
        }

        var id = conversationId ?? Guid.NewGuid();
        var key = GetKey(userId, organizationId, id);
        var state = this.GetState(key, conversationId is null);
        if (state is null)
        {
            return new AiPilotChatResponse(id.ToString(), "Диалог истёк или не найден. Начните новый, чтобы не потерять контекст.", false);
        }

        await state.Gate.WaitAsync(cancellationToken);
        try
        {
            if (IsConversationCancellation(text))
            {
                const string cancellationReply = "Черновик отменён, CRM не изменялась. Можете сказать новую команду.";
                state.History.Clear();
                state.HistoryCharacters = 0;
                state.UserTurns = 0;
                state.PendingDraft = null;
                state.PendingCommand = null;
                state.RequestedSlot = null;
                if (!this.TryAddMessage(state, "user", text) || !this.TryAddMessage(state, "assistant", cancellationReply))
                {
                    this.Save(key, state);
                    return ContextLimitResponse(id);
                }

                state.UserTurns = 1;
                this.Save(key, state);

                return new AiPilotChatResponse(id.ToString(), cancellationReply, false);
            }

            if (state.UserTurns >= router.RuntimeOptions.MaximumUserTurns || state.HistoryCharacters + text.Length > router.RuntimeOptions.MaximumHistoryCharacters)
            {
                this.Save(key, state);
                return new AiPilotChatResponse(id.ToString(), "В этом диалоге закончился лимит контекста. Начните новый, чтобы продолжить без потери деталей.", false);
            }

            if (IsGreeting(text))
            {
                state.UserTurns++;
                var greeting = "Здравствуйте! Я помогу найти автомобиль и подготовить запись, визит, работы, оплату, перенос или другое действие. Скажите, что нужно сделать.";
                if (!this.TryAddMessage(state, "user", text) || !this.TryAddMessage(state, "assistant", greeting))
                {
                    this.Save(key, state);
                    return ContextLimitResponse(id);
                }

                this.Save(key, state);
                return new AiPilotChatResponse(id.ToString(), greeting, false);
            }

            if (IsThanks(text) || IsConfirmation(text))
            {
                state.UserTurns++;
                var reply = state.PendingDraft is not null
                    ? "Черновик подготовлен, но ещё не выполнен. Проверьте его и нажмите кнопку подтверждения в приложении."
                    : "Пожалуйста! Изменения появятся только после подтверждения черновика.";
                if (!this.TryAddMessage(state, "user", text) || !this.TryAddMessage(state, "assistant", reply))
                {
                    this.Save(key, state);
                    return ContextLimitResponse(id);
                }

                this.Save(key, state);
                return new AiPilotChatResponse(id.ToString(), reply, false, state.PendingDraft);
            }

            var previousCommand = state.PendingCommand;
            var previousSlot = state.RequestedSlot;
            var startsNewTask = IsNewTask(text);
            if (startsNewTask)
            {
                previousCommand = null;
                previousSlot = null;
            }

            var previousHistoryCount = state.History.Count;
            var previousHistoryCharacters = state.HistoryCharacters;
            var previousUserTurns = state.UserTurns;
            state.UserTurns++;
            if (!this.TryAddMessage(state, "user", text))
            {
                this.Save(key, state);
                return ContextLimitResponse(id);
            }

            AiPilotParseResult parsed;
            try
            {
                parsed = previousCommand is not null && previousSlot is "registrationPlate.confirm" or "parameter:newRegistrationPlate" && IsPlateConfirmationReply(text)
                    ? new AiPilotParseResult(new AiPilotCommand { Action = previousCommand.Action }, null)
                    : previousCommand?.Action == "book" && previousSlot?.StartsWith("book.price:", StringComparison.Ordinal) == true &&
                    (IsDeferredPriceReply(text) || TryReadPriceReply(text, out _))
                    ? new AiPilotParseResult(new AiPilotCommand { Action = "book" }, null)
                    : await router.PrepareConversationAsync(state.History, today, cancellationToken, previousCommand, previousSlot);
            }
            catch
            {
                state.History.RemoveRange(previousHistoryCount, state.History.Count - previousHistoryCount);
                state.HistoryCharacters = previousHistoryCharacters;
                state.UserTurns = previousUserTurns;
                throw;
            }

            var continuesPendingCommand = previousCommand is not null && previousSlot is not null && !startsNewTask &&
                parsed.Command.Action != "unsupported";
            var command = continuesPendingCommand
                ? MergePendingCommand(previousCommand!, parsed.Command, previousSlot, text)
                : parsed.Command;
            if (previousCommand is null && command.Action == "service_add" && IsCompletedVisitUtterance(text))
            {
                command = ConvertServiceAddToVisit(command, text);
            }
            else if (previousCommand is null && command.Action == "payment_add" && IsUnattachedPaymentVisit(text))
            {
                command = ConvertPaymentAddToVisit(command, text);
            }

            state.PendingCommand = null;
            state.RequestedSlot = null;
            if (command.Action == "unsupported")
            {
                var unsupported = "Я могу помогать с записями, визитами, автомобилями, клиентами, работами и оплатами в рамках доступных действий. Административные права и тарифы менять не могу.";
                state.PendingDraft = null;
                state.PendingCommand = null;
                state.RequestedSlot = null;
                if (!this.TryAddMessage(state, "assistant", unsupported))
                {
                    this.Save(key, state);
                    return ContextLimitResponse(id);
                }

                this.Save(key, state);
                return new AiPilotChatResponse(id.ToString(), unsupported, false, Cost: parsed.Cost);
            }

            var question = this.GetMissingInformation(command, out var requestedSlot);
            if (question is not null)
            {
                state.PendingDraft = null;
                state.PendingCommand = command;
                state.RequestedSlot = requestedSlot;
                if (!this.TryAddMessage(state, "assistant", question))
                {
                    this.Save(key, state);
                    return ContextLimitResponse(id);
                }

                this.Save(key, state);
                return new AiPilotChatResponse(id.ToString(), question, true, Cost: parsed.Cost, ContinuesPendingCommand: continuesPendingCommand);
            }

            if (!IsStrictlyValid(command))
            {
                throw new InvalidOperationException("Не удалось проверить команду. Уточните запрос, изменения в CRM не выполнялись.");
            }

            var action = AiPilotActionCatalog.All.First(item => item.Action == command.Action);
            var warnings = GetWarnings(command);

            var draft = new AiPilotChatDraft(
                Guid.NewGuid().ToString("N"),
                command,
                string.IsNullOrWhiteSpace(command.RegistrationPlate) ? action.Title : $"{action.Title} · {command.RegistrationPlate}",
                warnings,
                parsed.Cost);
            state.PendingDraft = draft;
            state.PendingCommand = null;
            state.RequestedSlot = null;
            var responseMessage = $"Подготовила черновик: {draft.Summary}. Проверьте его и нажмите подтверждение, чтобы выполнить действие.";
            if (!this.TryAddMessage(state, "assistant", responseMessage))
            {
                state.PendingDraft = null;
                this.Save(key, state);
                return ContextLimitResponse(id);
            }

            this.Save(key, state);
            return new AiPilotChatResponse(id.ToString(), responseMessage, false, draft, parsed.Cost, continuesPendingCommand);
        }
        finally
        {
            state.Gate.Release();
        }
    }

    public async Task<string> AddFeedbackAsync(
        string userId,
        Guid organizationId,
        Guid conversationId,
        string kind,
        string message,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > router.RuntimeOptions.MaximumMessageCharacters || kind is not ("clarification" or "completed" or "failed"))
        {
            throw new InvalidOperationException("Не удалось обновить состояние разговора.");
        }

        var key = GetKey(userId, organizationId, conversationId);
        var state = this.GetState(key, false);
        if (state is null)
        {
            throw new InvalidOperationException("Диалог истёк или не найден. Начните новый.");
        }

        await state.Gate.WaitAsync(cancellationToken);
        try
        {
            var previousDraft = state.PendingDraft;
            var completedPlate = previousDraft?.Command.RegistrationPlate;
            var completedNote = kind == "completed" && !string.IsNullOrWhiteSpace(completedPlate)
                ? $"Интерфейс отметил предыдущий черновик выполненным; для новой команды можно использовать автомобиль {completedPlate}. Не повторяй прежнее действие. "
                : string.Empty;
            var boundedMessage = message[..Math.Min(message.Length, router.RuntimeOptions.MaximumMessageCharacters - 100)];
            var feedback = $"Результат интерфейса ({kind}): {boundedMessage}";
            if (state.HistoryCharacters + completedNote.Length + feedback.Length > router.RuntimeOptions.MaximumHistoryCharacters)
            {
                throw new InvalidOperationException("В этом диалоге закончился лимит контекста. Начните новый.");
            }

            state.PendingDraft = null;
            if (kind == "clarification" && previousDraft is not null)
            {
                state.PendingCommand = previousDraft.Command;
                state.RequestedSlot = null;
            }
            else
            {
                state.PendingCommand = null;
                state.RequestedSlot = null;
            }

            if (kind == "completed")
            {
                if (!string.IsNullOrWhiteSpace(completedPlate))
                {
                    this.TryAddMessage(state, "assistant", completedNote.Trim());
                }
            }

            this.TryAddMessage(state, "assistant", feedback);
            this.Save(key, state);

            return "Контекст разговора обновлён. Это сообщение не меняет данные CRM.";
        }
        finally
        {
            state.Gate.Release();
        }
    }

    private ConversationState? GetState(string key, bool create)
    {
        lock (s_stateCreationLock)
        {
            if (cache.TryGetValue(key, out ConversationState? state) && state is not null)
            {
                return state;
            }

            if (!create)
            {
                return null;
            }

            state = new ConversationState();
            cache.Set(key, state, TimeSpan.FromMinutes(router.RuntimeOptions.ConversationTtlMinutes));

            return state;
        }
    }

    private void Save(string key, ConversationState state) => cache.Set(key, state, TimeSpan.FromMinutes(router.RuntimeOptions.ConversationTtlMinutes));

    public static IReadOnlyList<string> GetWarnings(AiPilotCommand command)
    {
        var warnings = new List<string>();
        if (command.Action is "start" or "arrive" or "complete" or "visit" &&
            (command.StartTime is not null || command.EndTime is not null))
        {
            warnings.Add("В демо фактические часы не сохраняются как интервалы работы; действие меняет статус заказа.");
        }

        if (command.Services.Count > 1 && command.TotalAmount.HasValue && command.Services.Any(item => !item.Price.HasValue))
        {
            warnings.Add("Указана общая сумма без цены каждой работы. Проверьте состав черновика перед выполнением.");
        }

        if (command.PaidAmount.HasValue)
        {
            warnings.Add("Оплата учитывается отдельным запросом после сохранения заказа. Реальные деньги не списываются.");
        }

        if (AiPilotActionCatalog.TryGet(command.Action, out var action) && action.Destructive)
        {
            warnings.Add("Действие удаляет или отменяет данные. Перед выполнением проверьте цель и подтвердите изменение.");
        }

        return warnings;
    }

    private static AiPilotChatResponse ContextLimitResponse(Guid id) =>
        new(id.ToString(), "В диалоге закончился лимит контекста. Начните новый, чтобы продолжить без потери деталей.", false);

    private static string GetKey(string userId, Guid organizationId, Guid conversationId) =>
        $"{userId}:{organizationId:D}:{conversationId:D}";

    private bool TryAddMessage(ConversationState state, string role, string content)
    {
        if (state.HistoryCharacters + content.Length > router.RuntimeOptions.MaximumHistoryCharacters)
        {
            return false;
        }

        state.History.Add(new AiPilotConversationMessage(role, content));
        state.HistoryCharacters += content.Length;

        return true;
    }

    private static bool IsGreeting(string text) => text.Trim().ToLowerInvariant() is "привет" or "здравствуйте" or "добрый день" or "доброе утро" or "добрый вечер" or "что ты умеешь?";
    private static bool IsThanks(string text) => text.Trim().ToLowerInvariant() is "спасибо" or "благодарю" or "ок, спасибо";
    private static bool IsConfirmation(string text) => text.Trim().ToLowerInvariant() is "да" or "ага" or "ок" or "хорошо";
    private static bool IsConversationCancellation(string text) => text.Trim().ToLowerInvariant() is "отмена" or "не надо" or "забудь" or "начнём заново";

    private string? GetMissingInformation(AiPilotCommand command, out string? requestedSlot)
    {
        requestedSlot = null;
        var plateQuestion = registrationPlates is null ? null : AiPilotRegistrationPlateGuard.GetClarification(command, registrationPlates);
        if (plateQuestion is not null)
        {
            requestedSlot = plateQuestion.Field;

            return plateQuestion.Message;
        }
        if (command.Action is "visit" or "book" && command.Services.Count == 0)
        {
            requestedSlot = "visit.services";

            return command.Action switch
            {
                "visit" => "Какие работы выполнили?",
                "book" => "На какие работы записать автомобиль?",
                _ => "Какие работы будем делать для этого автомобиля?"
            };
        }

        if (command.Action == "visit" && command.Services.Any(service => !service.Price.HasValue) && !command.TotalAmount.HasValue)
        {
            var service = command.Services.First(item => !item.Price.HasValue);
            requestedSlot = "visit.price";

            return $"Сколько стоила работа «{service.Name}»?";
        }

        if (AiPilotActionCatalog.TryGet(command.Action, out var action) && action.RequiresPlate &&
            string.IsNullOrWhiteSpace(command.RegistrationPlate) && !HasValue(command.Parameters, "maintenanceId"))
        {
            requestedSlot = "registrationPlate";

            return "Какой госномер с регионом?";
        }

        if (command.Action is "book" or "reopen" or "reschedule" && string.IsNullOrWhiteSpace(command.Date))
        {
            requestedSlot = "date";

            return "На какую дату нужно записать или перенести автомобиль?";
        }

        if (command.Action == "payment_add" && !command.PaidAmount.HasValue && !HasValue(command.Parameters, "amount") && !command.TotalAmount.HasValue)
        {
            requestedSlot = "paymentAmount";

            return "Сколько денег получили?";
        }

        if (command.Action is "client_get" or "client_update" && string.IsNullOrWhiteSpace(command.RegistrationPlate) &&
            !HasValue(command.Parameters, "clientName") && !HasValue(command.Parameters, "phone"))
        {
            requestedSlot = "clientIdentity";

            return "Кого из клиентов найти? Назовите имя или телефон.";
        }

        if (command.Action is "vehicle_get" or "vehicle_update" or "vehicle_archive" or "vehicle_restore" &&
            string.IsNullOrWhiteSpace(command.RegistrationPlate) && !HasValue(command.Parameters, "name") && !HasValue(command.Parameters, "vin"))
        {
            requestedSlot = "vehicleIdentity";

            return "Какой автомобиль найти? Назовите госномер, марку и модель или VIN.";
        }

        if (!AiPilotActionCatalog.TryGet(command.Action, out action))
        {
            return null;
        }

        var missing = action.Parameters.FirstOrDefault(parameter => parameter.Required && !HasValue(command.Parameters, parameter.Name));
        if (missing is not null)
        {
            requestedSlot = "parameter:" + missing.Name;

            return QuestionFor(missing.Name, missing.Description);
        }

        return null;
    }

    private static AiPilotCommand MergePendingCommand(
        AiPilotCommand pending,
        AiPilotCommand parsed,
        string? requestedSlot,
        string userText)
    {
        var parameters = pending.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
        var allowedParameters = AiPilotActionCatalog.All.First(item => item.Action == pending.Action).Parameters
            .Select(parameter => parameter.Name)
            .ToHashSet(StringComparer.Ordinal);
        var parameterSlot = requestedSlot?.StartsWith("parameter:", StringComparison.Ordinal) == true
            ? requestedSlot["parameter:".Length..]
            : null;
        foreach (var pair in parsed.Parameters)
        {
            var isIdentityParameter = requestedSlot == "clientIdentity" && pair.Key is "clientName" or "phone"
                || requestedSlot == "vehicleIdentity" && pair.Key == "vin"
                || requestedSlot == "registrationPlate.confirm" && pair.Key == "vehicleKind";
            if (allowedParameters.Contains(pair.Key) && pair.Value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined &&
                (isIdentityParameter || parameterSlot == pair.Key))
            {
                parameters[pair.Key] = pair.Value.Clone();
            }
        }

        var registrationPlate = pending.RegistrationPlate;
        var confirmedRegistrationPlate = pending.ConfirmedRegistrationPlate;
        if (parameterSlot == "newRegistrationPlate" && !IsPlateConfirmationReply(userText) &&
            TryGetString(parameters, "newRegistrationPlate") != TryGetString(pending.Parameters, "newRegistrationPlate"))
        {
            confirmedRegistrationPlate = null;
        }
        var date = pending.Date;
        var startTime = pending.StartTime;
        var endTime = pending.EndTime;
        var services = pending.Services;
        var totalAmount = pending.TotalAmount;
        var paidAmount = pending.PaidAmount;
        var paymentMethod = pending.PaymentMethod;
        var comment = pending.Comment;

        if (requestedSlot is "registrationPlate.confirm" or "parameter:newRegistrationPlate" && IsPlateConfirmationReply(userText))
        {
            confirmedRegistrationPlate = requestedSlot == "registrationPlate.confirm"
                ? pending.RegistrationPlate
                : TryGetString(pending.Parameters, "newRegistrationPlate");
        }
        else if (requestedSlot is "registrationPlate" or "registrationPlate.confirm")
        {
            if (!string.IsNullOrWhiteSpace(parsed.RegistrationPlate))
            {
                registrationPlate = parsed.RegistrationPlate;
                if (!string.Equals(registrationPlate, pending.RegistrationPlate, StringComparison.Ordinal))
                {
                    parameters.Remove("maintenanceId");
                    parameters.Remove("recordDate");
                    parameters.Remove("latest");
                    confirmedRegistrationPlate = null;
                }
            }
        }
        else if (requestedSlot == "visit.services")
        {
            services = parsed.Services.Count > 0
                ? parsed.Services
                : TryGetString(parsed.Parameters, "serviceName") is { Length: > 0 } serviceName
                    ? [new AiPilotServiceLine { Name = serviceName }]
                    : pending.Services;
        }
        else if (requestedSlot == "visit.price")
        {
            var price = parsed.Services.FirstOrDefault(service => service.Price.HasValue)?.Price
                ?? parsed.TotalAmount
                ?? ReadDecimal(parsed.Parameters, "price");
            if (price.HasValue)
            {
                var serviceIndex = pending.Services.FindIndex(service => !service.Price.HasValue);
                if (serviceIndex >= 0)
                {
                    services = pending.Services
                        .Select((service, index) => index == serviceIndex
                            ? new AiPilotServiceLine { Name = service.Name, Price = price }
                            : service)
                        .ToList();
                }
                else
                {
                    totalAmount = price;
                }
            }
        }
        else if (pending.Action == "book" && requestedSlot?.StartsWith("book.price:", StringComparison.Ordinal) == true &&
            int.TryParse(requestedSlot["book.price:".Length..], out var priceIndex) && priceIndex >= 0 && priceIndex < pending.Services.Count)
        {
            var currentService = pending.Services[priceIndex];
            var isDeferred = IsDeferredPriceReply(userText);
            var matchingServices = parsed.Services
                .Where(service => string.Equals(service.Name.Trim(), currentService.Name.Trim(), StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToArray();

            var matchingService = parsed.Services.Count == pending.Services.Count &&
                string.Equals(parsed.Services[priceIndex].Name.Trim(), currentService.Name.Trim(), StringComparison.OrdinalIgnoreCase)
                    ? parsed.Services[priceIndex]
                    : matchingServices.Length == 1 ? matchingServices[0] : null;
            var isTotalReply = pending.Services.Count > 1 && s_totalPriceReplyPattern.IsMatch(userText);
            decimal? price = null;
            if (TryReadPriceReply(userText, out var statedPrice))
            {
                price = statedPrice;
            }
            else if (!isTotalReply)
            {
                price = matchingService?.Price
                    ?? (pending.Services.Count == 1 || parsed.TotalAmount != pending.TotalAmount ? parsed.TotalAmount : null);
            }
            if (isDeferred || price.HasValue)
            {
                services = pending.Services.Select((service, index) => index == priceIndex
                    ? new AiPilotServiceLine { Name = service.Name, Price = isDeferred ? null : price, IsPriceDeferred = isDeferred }
                    : service).ToList();
            }
        }
        else if (requestedSlot == "paymentAmount")
        {
            paidAmount = parsed.PaidAmount ?? parsed.TotalAmount ?? ReadDecimal(parsed.Parameters, "amount");
            paymentMethod = parsed.PaymentMethod ?? pending.PaymentMethod;
            if (IsExplicitNoPayment(userText))
            {
                paidAmount = pending.PaidAmount;
            }
        }
        else if (requestedSlot == "date")
        {
            date = parsed.Date ?? pending.Date;
            startTime = parsed.StartTime ?? pending.StartTime;
            endTime = parsed.EndTime ?? pending.EndTime;
        }
        else if (requestedSlot is "clientIdentity" or "vehicleIdentity")
        {
            registrationPlate = parsed.RegistrationPlate ?? pending.RegistrationPlate;
        }

        if (!string.IsNullOrWhiteSpace(registrationPlate) &&
            !string.Equals(registrationPlate, pending.RegistrationPlate, StringComparison.Ordinal))
        {
            parameters.Remove("maintenanceId");
            parameters.Remove("recordDate");
            parameters.Remove("latest");
            confirmedRegistrationPlate = null;
        }

        if (requestedSlot is "visit.services" or "visit.price")
        {
            registrationPlate = parsed.RegistrationPlate ?? pending.RegistrationPlate;
            date = parsed.Date ?? pending.Date;
            totalAmount = parsed.TotalAmount ?? totalAmount;
            if (parsed.PaidAmount.HasValue && IsExplicitPayment(userText) && !IsExplicitNoPayment(userText))
            {
                paidAmount = parsed.PaidAmount;
            }

            paymentMethod = parsed.PaymentMethod ?? pending.PaymentMethod;
        }

        if (requestedSlot == "parameter:comment")
        {
            comment = parsed.Comment ?? pending.Comment;
        }

        return new AiPilotCommand
        {
            Action = pending.Action,
            RegistrationPlate = registrationPlate,
            ConfirmedRegistrationPlate = confirmedRegistrationPlate,
            Date = date,
            StartTime = startTime,
            EndTime = endTime,
            Services = services,
            TotalAmount = totalAmount,
            PaidAmount = paidAmount,
            PaymentMethod = paymentMethod,
            Comment = comment,
            Parameters = parameters
        };
    }

    private static bool IsNewTask(string text) => s_newTaskPrefix.IsMatch(text.Trim());

    private static bool IsDeferredPriceReply(string text) => text.Trim().TrimEnd('.', '!', '?').ToLowerInvariant() is
        "неизвестно" or "пока неизвестно" or "не знаю" or "пока не знаю" or "цена неизвестна" or "цена пока неизвестна" or
        "стоимость неизвестна" or "стоимость пока неизвестна" or "уточним позже" or "цену уточним позже" or "без цены" or "пока без цены";

    private static bool IsPlateConfirmationReply(string text) => text.Trim().TrimEnd('.', '!', '?').ToLowerInvariant() is
        "номер верный" or "номер правильный" or "да, номер верный" or "да, номер правильный";

    private static bool TryReadPriceReply(string text, out decimal price)
    {
        price = default;
        var match = s_priceReplyPattern.Match(text.Trim());
        if (!match.Success)
        {
            return false;
        }

        var amount = match.Groups["amount"].Value.Replace(" ", string.Empty).Replace("\u00A0", string.Empty).Replace(',', '.');

        return decimal.TryParse(amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out price);
    }

    private static bool IsCompletedVisitUtterance(string text)
    {
        var normalized = text.Trim();
        if (!s_completedVisitPrefix.IsMatch(normalized))
        {
            return false;
        }

        return !normalized.Contains("в существующем заказе", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains("к заказу", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains("в заказ", StringComparison.OrdinalIgnoreCase);
    }

    private static AiPilotCommand ConvertServiceAddToVisit(AiPilotCommand command, string text)
    {
        var serviceName = TryGetString(command.Parameters, "serviceName");
        if (string.IsNullOrWhiteSpace(serviceName) && command.Services.Count == 0)
        {
            return command;
        }

        var price = ReadDecimal(command.Parameters, "price");
        var services = command.Services.Count > 0
            ? command.Services
            : [new AiPilotServiceLine { Name = serviceName!, Price = price }];
        var parameters = command.Parameters
            .Where(pair => pair.Key is "maintenanceId" or "recordDate" or "latest")
            .ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);

        return new AiPilotCommand
        {
            Action = "visit",
            RegistrationPlate = command.RegistrationPlate,
            Date = command.Date,
            StartTime = command.StartTime,
            EndTime = command.EndTime,
            Services = services,
            TotalAmount = command.TotalAmount ?? price,
            PaidAmount = IsExplicitPayment(text) && !IsExplicitNoPayment(text) ? command.PaidAmount : null,
            PaymentMethod = command.PaymentMethod,
            Comment = command.Comment,
            Parameters = parameters
        };
    }

    private static bool IsUnattachedPaymentVisit(string text)
    {
        var normalized = text.Trim().ToLowerInvariant();
        if (IsExplicitNoPayment(normalized) ||
            !normalized.Contains("взял", StringComparison.Ordinal) &&
            !normalized.Contains("получил", StringComparison.Ordinal))
        {
            return false;
        }

        return !normalized.Contains("прими оплат", StringComparison.Ordinal)
            && !normalized.Contains("учти оплат", StringComparison.Ordinal)
            && !normalized.Contains("добавь оплат", StringComparison.Ordinal)
            && !normalized.Contains("запиши оплат", StringComparison.Ordinal)
            && !normalized.Contains("оплата по заказ", StringComparison.Ordinal)
            && !normalized.Contains("платёж по заказ", StringComparison.Ordinal)
            && !normalized.Contains("платеж по заказ", StringComparison.Ordinal)
            && !normalized.Contains("заказ", StringComparison.Ordinal);
    }

    private static AiPilotCommand ConvertPaymentAddToVisit(AiPilotCommand command, string text)
    {
        var amount = command.PaidAmount
            ?? ReadDecimal(command.Parameters, "amount")
            ?? command.TotalAmount;
        var parameters = command.Parameters
            .Where(pair => pair.Key is "maintenanceId" or "recordDate" or "latest")
            .ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);

        return new AiPilotCommand
        {
            Action = "visit",
            RegistrationPlate = command.RegistrationPlate,
            Date = command.Date,
            StartTime = command.StartTime,
            EndTime = command.EndTime,
            Services = [],
            TotalAmount = amount,
            PaidAmount = IsExplicitNoPayment(text) ? null : amount,
            PaymentMethod = command.PaymentMethod,
            Comment = command.Comment,
            Parameters = parameters
        };
    }

    private static bool IsExplicitNoPayment(string text)
    {
        var normalized = text.Trim().ToLowerInvariant();

        return normalized.Contains("не брали", StringComparison.Ordinal)
            || normalized.Contains("не взял", StringComparison.Ordinal)
            || normalized.Contains("не взяли", StringComparison.Ordinal)
            || normalized.Contains("не принимали", StringComparison.Ordinal)
            || normalized.Contains("не приняли", StringComparison.Ordinal)
            || normalized.Contains("не платили", StringComparison.Ordinal)
            || normalized.Contains("не оплат", StringComparison.Ordinal)
            || normalized.Contains("не получил", StringComparison.Ordinal)
            || normalized.Contains("без оплаты", StringComparison.Ordinal)
            || normalized.Contains("деньги не", StringComparison.Ordinal);
    }

    private static bool IsExplicitPayment(string text)
    {
        var normalized = text.Trim().ToLowerInvariant();

        return normalized.Contains("взял", StringComparison.Ordinal)
            || normalized.Contains("получил", StringComparison.Ordinal)
            || normalized.Contains("принял оплат", StringComparison.Ordinal)
            || normalized.Contains("оплатил", StringComparison.Ordinal)
            || normalized.Contains("наличными", StringComparison.Ordinal)
            || normalized.Contains("картой", StringComparison.Ordinal)
            || normalized.Contains("переводом", StringComparison.Ordinal);
    }

    private static decimal? ReadDecimal(Dictionary<string, JsonElement> parameters, string name) =>
        parameters.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
            ? number
            : null;

    private static string? TryGetString(Dictionary<string, JsonElement> parameters, string name) =>
        parameters.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string QuestionFor(string name, string description) => name switch
    {
        "name" => "Как называется клиент, автомобиль или позиция каталога?",
        "serviceName" => "Как называется работа?",
        "price" => "Сколько стоит эта работа?",
        "amount" => "Какую сумму указать?",
        "date" => "На какую дату?",
        "minutes" => "Сколько минут заняла работа?",
        "reason" => "Укажите причину действия.",
        "comment" => "Какой комментарий записать?",
        "decision" => "Подтвердить или отклонить предложение?",
        _ => $"Уточните, пожалуйста: {description}"
    };

    private static bool HasValue(Dictionary<string, JsonElement> parameters, string name) =>
        parameters.TryGetValue(name, out var value) && value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined &&
        (value.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(value.GetString()));

    private static bool IsStrictlyValid(AiPilotCommand command)
    {
        if (!RouterAiPilotClient.IsValidConversationCommand(command, true) ||
            !AiPilotActionCatalog.TryGet(command.Action, out var action) ||
            !AiPilotActionCatalog.HasValidParameters(new AiPilotActionCommand(action, command.Parameters)) ||
            action.RequiresPlate && string.IsNullOrWhiteSpace(command.RegistrationPlate) && !HasValue(command.Parameters, "maintenanceId"))
        {
            return false;
        }

        if (command.Action == "visit" && (command.Services.Count == 0 || command.Services.Any(service => !service.Price.HasValue) && !command.TotalAmount.HasValue))
        {
            return false;
        }

        return true;
    }

    private sealed class ConversationState
    {
        public List<AiPilotConversationMessage> History { get; } = [];
        public int UserTurns { get; set; }
        public int HistoryCharacters { get; set; }
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public AiPilotChatDraft? PendingDraft { get; set; }
        public AiPilotCommand? PendingCommand { get; set; }
        public string? RequestedSlot { get; set; }
    }
}
