using System.Globalization;
using System.Text.Json;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Maintenance.Contracts.Imports;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Maintenance.Rules;
using OpenCRM.Services.Interfaces.Imports;
using Microsoft.EntityFrameworkCore;

namespace OpenCRM.Modules.Maintenance.Imports;

/// <summary>Проверяет и напрямую сохраняет исторические агрегаты обслуживания без командных побочных эффектов.</summary>
public sealed class ImportBusinessRecordProcessor(OpenCRMContext context) : IImportBusinessRecordProcessor
{
    private const int MaximumBatchRows = 1500;
    private const int MaximumBatchOrders = 25;
    private const int MaximumChildrenPerOrder = 1000;
    private const int MaximumReportIds = 100;
    private const decimal MaximumMoneyAmount = 9_999_999_999_999_999.99m;
    private static readonly string[] s_dateFormats =
    [
        "yyyy-MM-dd",
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"
    ];

    /// <inheritdoc />
    public Task ValidateAsync(
        string userId,
        Guid organizationId,
        IReadOnlyList<ImportBusinessRecord> records,
        IReadOnlyDictionary<string, Guid> vehicleIds,
        IReadOnlyDictionary<string, Guid> clientIds,
        IReadOnlyDictionary<string, Guid> orderIds,
        CancellationToken cancellationToken)
    {
        var normalizedVehicleIds = NormalizeIds(vehicleIds);
        var normalizedClientIds = NormalizeIds(clientIds);
        var normalizedOrderIds = NormalizeIds(orderIds);

        foreach (var record in records)
        {
            if (!IsBusinessKind(record.Kind))
            {
                continue;
            }

            record.MessageDetails = null;
            if (record.Excluded)
            {
                record.MessageCode = null;
                record.MessageParameters = null;
                continue;
            }

            record.Status = "create";
            record.Message = string.Empty;
            record.MessageCode = ImportRowMessageCodes.WillCreate;
            record.MessageParameters = null;
        }

        foreach (var duplicateTarget in records.Where(record => record.TargetId.HasValue)
            .GroupBy(record => record.TargetId!.Value)
            .Where(group => group.Count() > 1))
        {
            foreach (var record in duplicateTarget)
            {
                MarkError(record, "Идентификатор целевой записи повторяется в пакете.", ImportBusinessMessageCodes.DuplicateTargetId);
            }
        }

        var orders = records.Where(record => record.Kind == "order" && !record.Excluded).ToList();
        var duplicateOrderIds = orders
            .Where(order => !string.IsNullOrWhiteSpace(order.ExternalId))
            .GroupBy(order => NormalizeKey(order.ExternalId!), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newOrderByExternalId = new Dictionary<string, ImportBusinessRecord>(StringComparer.OrdinalIgnoreCase);
        var rowsByOrder = records
            .Where(record => IsChildKind(record.Kind) && !record.Excluded)
            .GroupBy(record => NormalizeKey(record.OrderExternalId ?? string.Empty), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var order in orders)
        {
            var externalId = NormalizeKey(order.ExternalId ?? string.Empty);
            if (externalId.Length == 0)
            {
                MarkError(order, "Укажите внешний идентификатор заказа.", ImportBusinessMessageCodes.OrderExternalIdRequired);
                continue;
            }

            if (duplicateOrderIds.Contains(externalId))
            {
                MarkError(order, "В пакете повторяется внешний идентификатор заказа.", ImportBusinessMessageCodes.DuplicateOrderExternalId);
                continue;
            }

            newOrderByExternalId[externalId] = order;
            if (order.OrderComment?.Length > 4000)
            {
                MarkError(order, "Комментарий заказа не должен превышать 4000 символов.", ImportBusinessMessageCodes.OrderCommentTooLong);
            }

            ValidateClientReference(order, normalizedClientIds);

            if (!TryGetId(normalizedVehicleIds, order.VehicleExternalId, out _))
            {
                MarkError(order, "Не найдена техника с указанным внешним идентификатором.", ImportBusinessMessageCodes.VehicleReferenceNotFound);
            }

            if (!TryParseDate(order.OccurredAt, out _))
            {
                MarkError(order, "Укажите дату заказа в формате ГГГГ-ММ-ДД или ISO 8601.", ImportBusinessMessageCodes.OrderDateInvalid);
            }

            if (!TryParseOrderStatus(order.OrderStatus, out _))
            {
                MarkError(order, "Поддерживаются только статусы completed и cancelled; плановые и активные записи не импортируются.", ImportBusinessMessageCodes.OrderStatusInvalid);
            }

            if (!TryParseOrderType(order.OrderType, out _))
            {
                MarkError(order, "Укажите известный тип обслуживания: maintenance, repair, refill или other.", ImportBusinessMessageCodes.OrderTypeInvalid);
            }

            if (!TryParseOptionalMoney(order.DiscountAmount, out var discount) || discount < 0)
            {
                MarkError(order, "Скидка должна быть неотрицательной суммой с точностью до копеек.", ImportBusinessMessageCodes.DiscountInvalid);
            }

            var children = rowsByOrder.GetValueOrDefault(externalId, []);
            if (children.Count > MaximumChildrenPerOrder)
            {
                MarkError(order, "В одном заказе больше 1000 дочерних строк. Разделите заказ перед импортом.", ImportBusinessMessageCodes.TooManyOrderChildren);
                foreach (var child in children)
                {
                    MarkError(child, "Заказ содержит больше 1000 дочерних строк.", ImportBusinessMessageCodes.TooManyOrderChildrenOnChild);
                }
            }

            var lines = children.Where(record => record.Kind == "order-line").ToList();
            if (lines.Count == 0)
            {
                MarkError(order, "Для заказа нужна хотя бы одна строка работ или услуг.", ImportBusinessMessageCodes.OrderLineRequired);
            }

            var grossAmount = 0m;
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line.DetailName))
                {
                    MarkError(line, "Укажите название работы или услуги.", ImportBusinessMessageCodes.OrderLineNameRequired);
                }

                if (!int.TryParse(line.Quantity, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity) || quantity < 1)
                {
                    MarkError(line, "Количество должно быть положительным целым числом.", ImportBusinessMessageCodes.OrderLineQuantityInvalid);
                    continue;
                }

                if (!TryParseMoney(line.UnitPrice, out var unitPrice) || unitPrice < 0)
                {
                    MarkError(line, "Цена единицы должна быть неотрицательной суммой с точностью до копеек.", ImportBusinessMessageCodes.OrderLineUnitPriceInvalid);
                    continue;
                }

                if (unitPrice != 0 && quantity > MaximumMoneyAmount / unitPrice)
                {
                    MarkError(line, "Сумма строки превышает допустимый диапазон.", ImportBusinessMessageCodes.OrderLineAmountOutOfRange);
                    continue;
                }

                var lineAmount = unitPrice * quantity;
                try
                {
                    ValidateDetailFields(line, lineAmount, quantity);
                }
                catch (InvalidOperationException exception)
                {
                    MarkError(line, exception.Message);
                    continue;
                }
                if (grossAmount > MaximumMoneyAmount - lineAmount)
                {
                    MarkError(line, "Сумма строк заказа превышает допустимый денежный диапазон.", ImportBusinessMessageCodes.OrderLineTotalOutOfRange);
                    continue;
                }

                grossAmount += lineAmount;
            }

            var payments = children.Where(record => record.Kind == "payment").ToList();
            var paidAmount = 0m;
            foreach (var payment in payments)
            {
                if (!TryParseMoney(payment.Amount, out var amount) || amount <= 0)
                {
                    MarkError(payment, "Сумма оплаты должна быть положительной и иметь точность до копеек.", ImportBusinessMessageCodes.PaymentAmountInvalid);
                }

                if (!TryParseDate(payment.OccurredAt, out var paidAt))
                {
                    MarkError(payment, "Укажите дату оплаты в формате ГГГГ-ММ-ДД или ISO 8601.", ImportBusinessMessageCodes.PaymentDateInvalid);
                }

                if (!string.IsNullOrWhiteSpace(payment.PaymentMethod) && !IsPaymentMethod(payment.PaymentMethod))
                {
                    MarkError(payment, "Способ оплаты должен быть cash, card, bank или other.", ImportBusinessMessageCodes.PaymentMethodInvalid);
                }

                if (payment.ActorName?.Length > 256 || CreatePaymentComment(payment).Length > 1000)
                {
                    MarkError(payment, "Имя кассира должно быть не длиннее 256 символов, комментарий оплаты — 1000.", ImportBusinessMessageCodes.PaymentMetadataTooLong);
                }

                ValidateClientReference(payment, normalizedClientIds);

                var cancelled = !string.IsNullOrWhiteSpace(payment.CancelledAt);
                if (cancelled && (!TryParseDate(payment.CancelledAt, out var cancelledAt) ||
                    TryParseDate(payment.OccurredAt, out paidAt) && cancelledAt < paidAt))
                {
                    MarkError(payment, "Дата отмены оплаты не может быть раньше даты оплаты и должна быть в ISO-формате.", ImportBusinessMessageCodes.PaymentCancellationDateInvalid);
                }

                if (!cancelled && TryParseMoney(payment.Amount, out amount) && amount > 0)
                {
                    if (paidAmount > MaximumMoneyAmount - amount)
                    {
                        MarkError(payment, "Сумма оплат превышает допустимый денежный диапазон.", ImportBusinessMessageCodes.PaymentTotalOutOfRange);
                    }
                    else
                    {
                        paidAmount += amount;
                    }
                }
            }

            var history = children.Where(record => record.Kind == "history").ToList();
            foreach (var eventRecord in history)
            {
                if (!TryParseDate(eventRecord.OccurredAt, out _))
                {
                    MarkError(eventRecord, "Укажите дату события истории в формате ГГГГ-ММ-ДД или ISO 8601.", ImportBusinessMessageCodes.HistoryDateInvalid);
                }

                if (string.IsNullOrWhiteSpace(eventRecord.EventType) || eventRecord.EventType.Length > 64)
                {
                    MarkError(eventRecord, "Укажите тип события истории длиной не более 64 символов.", ImportBusinessMessageCodes.HistoryTypeInvalid);
                }

                if (eventRecord.ActorName?.Length > 250)
                {
                    MarkError(eventRecord, "Имя автора события истории не должно превышать 250 символов.", ImportBusinessMessageCodes.HistoryAuthorTooLong);
                }
            }

            if (!TryParseOptionalMoney(order.DiscountAmount, out discount) || discount > grossAmount)
            {
                MarkError(order, "Скидка не может превышать сумму строк заказа.", ImportBusinessMessageCodes.DiscountExceedsOrderLines);
            }

            var netAmount = grossAmount - (TryParseOptionalMoney(order.DiscountAmount, out discount) ? discount : 0);
            if (!string.IsNullOrWhiteSpace(order.Amount) &&
                (!TryParseMoney(order.Amount, out var expectedAmount) || expectedAmount != netAmount))
            {
                MarkError(order, "Итоговая сумма заказа не совпадает с суммой строк за вычетом скидки.", ImportBusinessMessageCodes.OrderAmountMismatch);
            }

            if (paidAmount > netAmount)
            {
                MarkError(order, "Сумма действующих оплат превышает итог заказа.", ImportBusinessMessageCodes.PaymentsExceedOrderAmount);
            }

            if (TryParseOrderStatus(order.OrderStatus, out var status) && status == MaintenanceStatus.Cancelled &&
                payments.Any(payment => string.IsNullOrWhiteSpace(payment.CancelledAt)))
            {
                MarkError(order, "Для отменённого заказа все импортируемые оплаты должны быть отменены.", ImportBusinessMessageCodes.CancelledOrderHasActivePayments);
            }
        }

        foreach (var child in records.Where(record => IsChildKind(record.Kind) && !record.Excluded))
        {
            var orderExternalId = NormalizeKey(child.OrderExternalId ?? string.Empty);
            if (orderExternalId.Length == 0)
            {
                MarkError(child, "Укажите внешний идентификатор связанного заказа.", ImportBusinessMessageCodes.ChildOrderExternalIdRequired);
                continue;
            }

            if (!newOrderByExternalId.ContainsKey(orderExternalId) && !TryGetId(normalizedOrderIds, orderExternalId, out _))
            {
                MarkError(child, "Связанный заказ не найден в пакете или журнале импорта.", ImportBusinessMessageCodes.ChildOrderNotFound);
            }
        }

        return Task.CompletedTask;

    }

    /// <inheritdoc />
    public async Task<ImportBusinessRecordResult> ApplyAsync(
        string userId,
        Guid organizationId,
        IReadOnlyList<ImportBusinessRecord> records,
        IReadOnlyDictionary<string, Guid> vehicleIds,
        IReadOnlyDictionary<string, Guid> clientIds,
        IDictionary<string, Guid> orderIds,
        CancellationToken cancellationToken)
    {
        var currencyCode = await context.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.CurrencyCode)
            .SingleAsync(cancellationToken);
        if (currencyCode != CurrencyCodes.Rub)
        {
            throw new InvalidDataException("Формат импорта не содержит валюту. Для этой организации импорт денежных сумм невозможен.");
        }

        var pendingOrders = records.Where(record => record.Kind == "order" && !record.Excluded && record.Status == "create")
            .Where(record => !TryGetId(orderIds, record.ExternalId, out _))
            .ToList();
        var activeRows = records.Count(record => IsBusinessKind(record.Kind) && !record.Excluded && record.Status == "create");
        if (activeRows > MaximumBatchRows || pendingOrders.Count > MaximumBatchOrders)
        {
            throw new InvalidDataException("Размер пакета обычно не должен превышать 500 строк; абсолютный предел — 1500 строк и 25 заказов, чтобы не разрывать крупные агрегаты. Разбейте данные по заказам.");
        }

        var externalIds = pendingOrders.Select(order => NormalizeKey(order.ExternalId ?? string.Empty)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var childrenByOrder = records
            .Where(record => IsChildKind(record.Kind) && !record.Excluded && record.Status == "create" &&
                externalIds.Contains(NormalizeKey(record.OrderExternalId ?? string.Empty)))
            .GroupBy(record => NormalizeKey(record.OrderExternalId ?? string.Empty), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var vehicleTargetIds = pendingOrders
            .Select(order => TryGetId(vehicleIds, order.VehicleExternalId, out var vehicleId) ? vehicleId : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToHashSet();
        var activeVehicleIds = await context.Vehicles.AsNoTracking()
            .Where(vehicle => vehicle.OrganizationId == organizationId && !vehicle.IsDeleted && vehicleTargetIds.Contains(vehicle.Id))
            .Select(vehicle => vehicle.Id)
            .ToListAsync(cancellationToken);
        if (activeVehicleIds.Count != vehicleTargetIds.Count)
        {
            throw new InvalidDataException("Перед применением пакета проверьте, что все связанные автомобили активны и принадлежат выбранной организации.");
        }

        var normalizedClientIds = NormalizeIds(clientIds);
        var clientTargetIds = pendingOrders
            .Concat(childrenByOrder.Values.SelectMany(rows => rows).Where(record => record.Kind == "payment"))
            .Where(record => !string.IsNullOrWhiteSpace(record.ClientExternalId))
            .Select(record => TryGetId(normalizedClientIds, record.ClientExternalId, out var clientId) ? clientId : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToHashSet();
        var organizationClients = await context.OrganizationClients.AsNoTracking()
            .Where(client => client.OrganizationId == organizationId && !client.IsDeleted && clientTargetIds.Contains(client.Id))
            .ToDictionaryAsync(client => client.Id, cancellationToken);
        if (organizationClients.Count != clientTargetIds.Count)
        {
            throw new InvalidDataException("Указанный покупатель или плательщик отсутствует или относится к другой организации.");
        }

        var result = new ImportBusinessRecordResult();
        foreach (var orderRecord in pendingOrders)
        {
            var externalId = NormalizeKey(orderRecord.ExternalId ?? string.Empty);
            var orderId = GetTargetId(orderRecord);
            if (!TryGetId(vehicleIds, orderRecord.VehicleExternalId, out var vehicleId))
            {
                throw new InvalidDataException("Перед применением заказа не найден идентификатор связанного автомобиля.");
            }

            var buyerId = GetExplicitClientId(orderRecord, normalizedClientIds, organizationClients);
            var buyerName = orderRecord.PartyName?.Trim();

            var children = childrenByOrder.GetValueOrDefault(externalId, []);
            var lineRecords = children.Where(record => record.Kind == "order-line").ToList();
            var paymentRecords = children.Where(record => record.Kind == "payment").ToList();
            var historyRecords = children.Where(record => record.Kind == "history").ToList();
            if (children.Count > MaximumChildrenPerOrder)
            {
                throw new InvalidDataException("В одном заказе больше 1000 дочерних строк. Разделите заказ перед импортом.");
            }

            if (lineRecords.Count == 0)
            {
                throw new InvalidDataException("В пакет заказа не включена ни одна строка работ или услуг.");
            }

            var details = new List<MaintenanceDetail>(lineRecords.Count);
            var grossAmount = 0m;
            for (var position = 0; position < lineRecords.Count; position++)
            {
                var record = lineRecords[position];
                if (!TryParseMoney(record.UnitPrice, out var unitPrice) || unitPrice < 0 ||
                    !int.TryParse(record.Quantity, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity) || quantity < 1)
                {
                    throw new InvalidDataException("Невозможно применить некорректную цену или количество строки заказа.");
                }

                if (unitPrice != 0 && quantity > MaximumMoneyAmount / unitPrice)
                {
                    throw new InvalidDataException("Сумма строки заказа превышает допустимый денежный диапазон.");
                }

                var lineAmount = unitPrice * quantity;
                ValidateDetailFields(record, lineAmount, quantity);
                if (grossAmount > MaximumMoneyAmount - lineAmount)
                {
                    throw new InvalidDataException("Сумма строк заказа превышает допустимый денежный диапазон.");
                }

                grossAmount += lineAmount;
                var detailId = GetTargetId(record);
                result.RowTargets[record.Id] = detailId;
                result.CreatedOrderLines++;

                details.Add(new MaintenanceDetail
                {
                    DetailId = detailId,
                    MaintenanceId = orderId,
                    Position = position,
                    Name = record.DetailName?.Trim() ?? throw new InvalidDataException("У строки заказа отсутствует название."),
                    Description = record.DetailDescription,
                    Quantity = quantity,
                    Price = lineAmount
                });
            }

            var payments = paymentRecords.Select(record =>
            {
                if (!TryParseMoney(record.Amount, out var amount) || amount <= 0 ||
                    !TryParseDate(record.OccurredAt, out var occurredAt))
                {
                    throw new InvalidDataException("Невозможно применить некорректную дату или сумму оплаты.");
                }

                var cancelledAt = TryParseDate(record.CancelledAt, out var parsedCancelledAt) ? parsedCancelledAt : (DateTime?)null;
                var payment = new MaintenancePayment
                {
                    Id = GetTargetId(record),
                    MaintenanceId = orderId,
                    Amount = amount,
                    CurrencyCode = currencyCode,
                    MethodCode = NormalizePaymentMethod(record.PaymentMethod),
                    PayerClientId = GetExplicitClientId(record, normalizedClientIds, organizationClients),
                    PayerName = record.PartyName?.Trim(),
                    CreatedAt = occurredAt,
                    CreatedBy = userId,
                    Comment = CreatePaymentComment(record),
                    CancelledAt = cancelledAt,
                    CancelledBy = cancelledAt.HasValue ? userId : null,
                    CancellationReason = cancelledAt.HasValue ? "Отмена из исходной CRM" : null
                };
                result.RowTargets[record.Id] = payment.Id;
                AddFirst(result.PaymentIds, payment.Id);
                result.CreatedPayments++;

                return payment;
            }).ToList();
            var activePaidAmount = 0m;
            foreach (var payment in payments.Where(payment => !payment.CancelledAt.HasValue))
            {
                if (activePaidAmount > MaximumMoneyAmount - payment.Amount)
                {
                    throw new InvalidDataException("Сумма оплат превышает допустимый денежный диапазон.");
                }

                activePaidAmount += payment.Amount;
            }

            var occurred = ParseDate(orderRecord.OccurredAt!);
            var status = ParseOrderStatus(orderRecord.OrderStatus!);
            var discountAmount = TryParseMoney(orderRecord.DiscountAmount, out var parsedDiscount) ? parsedDiscount : 0;
            if (discountAmount < 0 || discountAmount > grossAmount || activePaidAmount > grossAmount - discountAmount)
            {
                throw new InvalidDataException("Скидка и сумма оплат должны соответствовать итогу строк заказа.");
            }

            var maintenance = new MaintenanceEntity
            {
                Id = orderId,
                CurrencyCode = currencyCode,
                UserId = userId,
                VehicleId = vehicleId,
                Status = status,
                Type = ParseOrderType(orderRecord.OrderType!),
                Date = occurred.Date,
                PlannedDate = status == MaintenanceStatus.Cancelled ? occurred.Date : null,
                CompletedDate = status == MaintenanceStatus.Completed ? occurred.Date : null,
                DateTimeCreation = occurred,
                Comment = orderRecord.OrderComment,
                BuyerClientId = buyerId,
                BuyerName = buyerName,
                DiscountAmount = discountAmount,
                PaidAmount = activePaidAmount,
                Details = details,
                HistoryEvents = historyRecords.Select(record => new MaintenanceHistoryEvent
                {
                    Id = GetTargetId(record),
                    MaintenanceId = orderId,
                    VehicleId = vehicleId,
                    OrganizationId = organizationId,
                    UserId = userId,
                    ActorDisplayName = string.IsNullOrWhiteSpace(record.ActorName) ? null : record.ActorName.Trim(),
                    EventType = "imported-history",
                    Title = $"История из прежней CRM: {record.EventType!.Trim()}",
                    Description = record.HistoryComment,
                    PayloadJson = CreateProvenancePayload(record.EventType, record.ActorName, record.ExternalId, record.Id),
                    CreatedAt = ParseDate(record.OccurredAt!)
                }).Append(new MaintenanceHistoryEvent
                {
                    Id = Guid.NewGuid(),
                    MaintenanceId = orderId,
                    VehicleId = vehicleId,
                    OrganizationId = organizationId,
                    UserId = userId,
                    EventType = "imported-history",
                    Title = "Заказ импортирован из прежней CRM",
                    PayloadJson = CreateProvenancePayload("order", orderRecord.ActorName, orderRecord.ExternalId, orderRecord.Id),
                    CreatedAt = occurred
                }).ToList()
            };

            context.Maintenance.Add(maintenance);
            context.MaintenancePayments.AddRange(payments);
            orderIds[externalId] = orderId;
            result.RowTargets[orderRecord.Id] = orderId;

            foreach (var historyRecord in historyRecords)
            {
                result.RowTargets[historyRecord.Id] = historyRecord.TargetId!.Value;
            }

            result.CreatedHistoryEvents += maintenance.HistoryEvents.Count;
            result.CreatedOrders++;
            AddFirst(result.OrderIds, orderId);
        }

        return result;
    }

    private static readonly HashSet<string> s_businessKinds = ["order", "order-line", "payment", "history"];

    private static void ValidateDetailFields(ImportBusinessRecord record, decimal lineAmount, int quantity)
    {
        MaintenanceDetailRules.MapDetails(new[]
        {
            new CreateMaintenanceDetailDto
            {
                Name = record.DetailName ?? string.Empty, Description = record.DetailDescription,
                Price = lineAmount, Quantity = quantity
            }
        });
    }

    private static bool IsBusinessKind(string kind) => s_businessKinds.Contains(kind);
    private static bool IsChildKind(string kind) => kind is "order-line" or "payment" or "history";
    private static string NormalizeKey(string value) => value.Trim();

    private static void MarkError(ImportBusinessRecord record, string message, string? messageCode = null)
    {
        record.Status = "error";
        record.Message = string.IsNullOrWhiteSpace(record.Message) ? message : $"{record.Message} {message}";
        record.MessageCode = null;
        record.MessageParameters = null;
        record.MessageDetails ??= [];
        record.MessageDetails.Add(new ImportRowMessageDetail { Message = message, MessageCode = messageCode });
    }

    private static void ValidateClientReference(ImportBusinessRecord record, IReadOnlyDictionary<string, Guid> clientIds)
    {
        if (record.PartyName?.Length > 250)
        {
            MarkError(record, "Имя покупателя или плательщика не должно превышать 250 символов.", ImportBusinessMessageCodes.PartyNameTooLong);
        }

        if (string.IsNullOrWhiteSpace(record.ClientExternalId))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(record.PartyName))
        {
            MarkError(record, "Для связанного клиента укажите имя из исходной записи, чтобы сохранить исторический снимок.", ImportBusinessMessageCodes.PartySnapshotRequired);
        }

        if (!TryGetId(clientIds, record.ClientExternalId, out _))
        {
            MarkError(record, "Связанный клиент не найден в импортированных клиентах или журнале импорта.", ImportBusinessMessageCodes.PartyClientNotFound);
        }
    }

    private static Guid? GetExplicitClientId(
        ImportBusinessRecord record,
        IReadOnlyDictionary<string, Guid> clientIds,
        IReadOnlyDictionary<Guid, OrganizationClient> organizationClients)
    {
        if (string.IsNullOrWhiteSpace(record.PartyName) && !string.IsNullOrWhiteSpace(record.ClientExternalId) ||
            record.PartyName?.Length > 250)
        {
            throw new InvalidDataException("Покупатель или плательщик не содержит допустимого исторического имени.");
        }

        if (string.IsNullOrWhiteSpace(record.ClientExternalId))
        {
            return null;
        }

        if (!TryGetId(clientIds, record.ClientExternalId, out var clientId) || !organizationClients.ContainsKey(clientId))
        {
            throw new InvalidDataException("Указанный покупатель или плательщик отсутствует или относится к другой организации.");
        }

        return clientId;
    }

    private static bool TryGetId(IEnumerable<KeyValuePair<string, Guid>> ids, string? externalId, out Guid id)
    {
        var key = NormalizeKey(externalId ?? string.Empty);
        if (key.Length == 0)
        {
            id = Guid.Empty;
            return false;
        }

        if (ids is IReadOnlyDictionary<string, Guid> readOnlyIds && readOnlyIds.TryGetValue(key, out id) ||
            ids is IDictionary<string, Guid> mutableIds && mutableIds.TryGetValue(key, out id))
        {
            return id != Guid.Empty;
        }

        foreach (var pair in ids)
        {
            if (string.Equals(NormalizeKey(pair.Key), key, StringComparison.OrdinalIgnoreCase))
            {
                id = pair.Value;
                return id != Guid.Empty;
            }
        }

        id = Guid.Empty;
        return false;
    }

    private static Dictionary<string, Guid> NormalizeIds(IEnumerable<KeyValuePair<string, Guid>> ids)
    {
        var normalizedIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in ids)
        {
            var key = NormalizeKey(pair.Key);
            if (key.Length == 0)
            {
                continue;
            }

            if (!normalizedIds.TryAdd(key, pair.Value) && normalizedIds[key] != pair.Value)
            {
                normalizedIds[key] = Guid.Empty;
            }
        }

        return normalizedIds;
    }

    private static bool TryParseMoney(string? value, out decimal amount)
    {
        return decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out amount) && amount >= -MaximumMoneyAmount && amount <= MaximumMoneyAmount &&
            decimal.Round(amount, 2, MidpointRounding.AwayFromZero) == amount;
    }

    private static bool TryParseOptionalMoney(string? value, out decimal amount)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            amount = 0;

            return true;
        }

        return TryParseMoney(value, out amount);
    }

    private static bool TryParseDate(string? value, out DateTime date)
    {
        if (DateTimeOffset.TryParseExact(value, s_dateFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            date = parsed.UtcDateTime;

            return true;
        }

        date = default;

        return false;
    }

    private static DateTime ParseDate(string value)
    {
        if (!TryParseDate(value, out var date))
        {
            throw new InvalidDataException("Дата исторической записи должна быть в однозначном формате ISO 8601.");
        }

        return date;
    }

    private static bool TryParseOrderStatus(string? value, out MaintenanceStatus status)
    {
        status = value?.Trim().ToLowerInvariant() switch
        {
            "completed" => MaintenanceStatus.Completed,
            "cancelled" => MaintenanceStatus.Cancelled,
            _ => default
        };

        return status is MaintenanceStatus.Completed or MaintenanceStatus.Cancelled;
    }

    private static MaintenanceStatus ParseOrderStatus(string value)
    {
        if (!TryParseOrderStatus(value, out var status))
        {
            throw new InvalidDataException("Поддерживаются только статусы completed и cancelled.");
        }

        return status;
    }

    private static bool TryParseOrderType(string? value, out MaintenanceType type)
    {
        type = value?.Trim().ToLowerInvariant() switch
        {
            "maintenance" => MaintenanceType.Maintenance,
            "repair" => MaintenanceType.Repair,
            "refill" => MaintenanceType.Refill,
            "other" => MaintenanceType.Other,
            _ => default
        };

        return value?.Trim().ToLowerInvariant() is "maintenance" or "repair" or "refill" or "other";
    }

    private static MaintenanceType ParseOrderType(string value)
    {
        if (!TryParseOrderType(value, out var type))
        {
            throw new InvalidDataException("Указан неизвестный тип обслуживания.");
        }

        return type;
    }

    private static bool IsPaymentMethod(string value) => NormalizePaymentMethod(value) is "cash" or "card" or "bank" or "other";

    private static string? NormalizePaymentMethod(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "cash" or "наличные" => "cash",
        "card" or "карта" => "card",
        "bank" or "перевод" => "bank",
        "other" or "другое" => "other",
        _ => value.Trim().ToLowerInvariant()
    };

    private static void AddFirst(List<Guid> ids, Guid id)
    {
        if (ids.Count < MaximumReportIds)
        {
            ids.Add(id);
        }
    }

    private static Guid GetTargetId(ImportBusinessRecord record)
    {
        record.TargetId ??= Guid.NewGuid();

        return record.TargetId.Value;
    }

    private static string CreatePaymentComment(ImportBusinessRecord record)
    {
        var comment = record.HistoryComment?.Trim();
        var actor = record.ActorName?.Trim();
        if (string.IsNullOrEmpty(actor))
        {
            return comment ?? string.Empty;
        }

        return string.IsNullOrEmpty(comment)
            ? $"Автор исходной CRM: {actor}"
            : $"{comment}\nАвтор исходной CRM: {actor}";
    }

    private static string CreateProvenancePayload(string? sourceEventType, string? sourceActor, string? sourceExternalId, string rowId) =>
        JsonSerializer.Serialize(new { sourceEventType, sourceActor, sourceExternalId, rowId });
}
