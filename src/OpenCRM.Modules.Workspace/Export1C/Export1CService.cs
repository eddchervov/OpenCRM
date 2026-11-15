using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Export1C;
using OpenCRM.Services.Interfaces.Export1C.Dtos;

namespace OpenCRM.Modules.Workspace.Export1C;

/// <summary> Сервис формирования пакета выгрузки данных в 1C. </summary>
public sealed class Export1CService(OpenCRMContext opencrmContext, Helpers.ProjectBrand? brand = null) : IExport1CService
{
    /// <summary> Формирует пакет выгрузки для 1C по заданным опциям. </summary>
    public async Task<Export1CPackageDto> CreateAsync(Guid organizationId, Export1COptionsDto options)
    {
        if (options.DateFrom == default || options.DateTo == default
            || options.DateTo == DateOnly.MaxValue || options.DateTo < options.DateFrom)
        {
            throw new ArgumentException("Укажите корректный период выгрузки.", nameof(options));
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(options.TimeZoneId, out var timeZone))
        {
            throw new ArgumentException("Неизвестный часовой пояс выгрузки.", nameof(options));
        }

        var organization = await opencrmContext.Organizations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == organizationId && !item.IsDeleted);
        if (organization == null)
        {
            throw new KeyNotFoundException("Организация не найдена.");
        }

        var from = options.DateFrom.ToDateTime(TimeOnly.MinValue);
        var toExclusive = options.DateTo.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var orderFrom = DateTime.SpecifyKind(from, DateTimeKind.Utc);
        var orderToExclusive = DateTime.SpecifyKind(toExclusive, DateTimeKind.Utc);
        var recordedFromUtc = TimeZoneInfo.ConvertTimeToUtc(from, timeZone);
        var recordedToUtc = TimeZoneInfo.ConvertTimeToUtc(toExclusive, timeZone);
        var exportedAtUtc = DateTime.UtcNow;

        var package = new Export1CPackageDto
        {
            ExportedAtUtc = exportedAtUtc,
            ReferenceDataSnapshotAtUtc = exportedAtUtc,
            DateFrom = options.DateFrom,
            DateTo = options.DateTo,
            TimeZoneId = options.TimeZoneId,
            ProjectName = brand?.DisplayName ?? Helpers.ProjectBrand.DefaultName,
            Organization = new Export1CPackageDto.OrganizationInfo
            {
                Id = organization.Id,
                Name = organization.Name,
                LegalName = organization.LegalName,
                Inn = organization.Inn,
                Kpp = organization.Kpp,
                Ogrn = organization.Ogrn,
                LegalAddress = organization.LegalAddress
            },
            MissingAccountingFields = options.DestinationCode == "bp3"
                ? [
                    "Для работ без указанной ставки НДС проверьте ошибки выгрузки.",
                    "Реквизиты клиента и способ оплаты могут отсутствовать.",
                    "Плательщик оплаты может быть не указан.",
                    "Дата платежа — дата его учёта в CRM, не подтверждение банковской операции."
                ]
                : []
        };
        if (options.IncludePayments)
        {
            package.Payments = await this.LoadPaymentsAsync(organizationId, recordedFromUtc, recordedToUtc);
            foreach (var payment in package.Payments)
            {
                payment.AccountingDate = DateOnly.FromDateTime(
                    TimeZoneInfo.ConvertTimeFromUtc(payment.RecordedAtUtc, timeZone));
            }
        }

        package.Orders = await this.LoadOrdersAsync(
            organizationId,
            options,
            orderFrom,
            orderToExclusive,
            package.Payments);
        foreach (var order in package.Orders)
        {
            order.AccountingDate = DateOnly.FromDateTime(order.CompletedDate ?? order.Date);
        }

        if (options.DestinationCode == "bp3")
        {
            package.ValidationIssues.AddRange(package.Orders
                .Where(item => !item.IsPaymentContextOnly && !item.ClientId.HasValue)
                .Select(item => new Export1CPackageDto.ValidationIssue
                {
                    EntityType = "order",
                    EntityId = item.Id,
                    OrderId = item.Id,
                    Message = "Заказ без покупателя. Проверьте, нужен ли он для загрузки в 1С."
                }));
            package.ValidationIssues.AddRange(package.Payments
                .Where(item => !item.PayerClientId.HasValue)
                .Select(item => new Export1CPackageDto.ValidationIssue
                {
                    EntityType = "payment",
                    EntityId = item.Id,
                    OrderId = item.OrderId,
                    Message = "Плательщик не установлен. Проверьте оплату перед загрузкой в 1С."
                }));
            package.ValidationIssues.AddRange(package.Payments
                .Where(item => item.IsOpeningBalance || item.CancelledAtUtc.HasValue
                    || item.Amount <= 0m || item.MethodCode is not ("cash" or "card" or "bank"))
                .Select(item => new Export1CPackageDto.ValidationIssue
                {
                    EntityType = "payment",
                    EntityId = item.Id,
                    OrderId = item.OrderId,
                    Message = item.IsOpeningBalance
                        ? "Начальный остаток не загружается как новая оплата в 1С. Проверьте его вручную."
                        : item.CancelledAtUtc.HasValue
                            ? "Оплата отменена. Проверьте ранее загруженный документ в 1С вручную."
                            : item.Amount <= 0m
                                ? "Сумма оплаты должна быть больше нуля для загрузки в 1С."
                                : "Способ оплаты не указан или не поддерживается загрузчиком 1С."
                }));
        }

        package.ValidationIssues.AddRange(package.Orders
            .Where(item => !item.IsPaymentContextOnly)
            .Where(item => item.Lines.Sum(line => line.TotalPrice ?? 0m) != item.GrossAmount)
            .Select(item => new Export1CPackageDto.ValidationIssue
            {
                EntityType = "order",
                EntityId = item.Id,
                OrderId = item.Id,
                Message = "Сумма округлённых строк не совпадает с итогом заказа. Проверьте цены старых работ."
            }));
        if (options.DestinationCode == "bp3")
        {
            package.ValidationIssues.AddRange(package.Orders
                .Where(item => !item.IsPaymentContextOnly && item.Status == MaintenanceStatus.Completed.ToString())
                .Where(item => item.GrossAmount <= 0m || item.NetAmount <= 0m || item.DiscountAmount > item.GrossAmount)
                .Select(item => new Export1CPackageDto.ValidationIssue
                {
                    EntityType = "order",
                    EntityId = item.Id,
                    OrderId = item.Id,
                    Message = "Сумма заказа после скидки должна быть больше нуля для загрузки реализации в 1С."
                }));
        }

        package.ValidationIssues.AddRange(package.Orders
            .Where(item => !item.IsPaymentContextOnly)
            .SelectMany(item => item.Lines
                .Where(line => !line.TotalPrice.HasValue)
                .Select(line => new Export1CPackageDto.ValidationIssue
                {
                    EntityType = "order_line",
                    EntityId = line.Id,
                    OrderId = item.Id,
                    Message = $"В заказе {item.Id} для работы «{line.Name}» не указана цена."
                })));
        package.ValidationIssues.AddRange(package.Orders
            .Where(item => !item.IsPaymentContextOnly)
            .SelectMany(item => item.Lines
                .Where(line => line.Quantity <= 0 || line.TotalPrice < 0m
                    || line.DiscountedTotalPrice < 0m || line.DiscountedTotalPrice > line.TotalPrice)
                .Select(line => new Export1CPackageDto.ValidationIssue
                {
                    EntityType = "order_line",
                    EntityId = line.Id,
                    OrderId = item.Id,
                    Message = $"Проверьте количество, цену и скидку работы «{line.Name}» перед выгрузкой."
                })));
        package.ValidationIssues.AddRange(package.Orders
            .Where(item => !item.IsPaymentContextOnly)
            .SelectMany(item => item.Lines
                .Where(line => !MaintenanceVatRates.IsValid(line.VatRateCode))
                .Select(line => new Export1CPackageDto.ValidationIssue
                {
                    EntityType = "order_line",
                    EntityId = line.Id,
                    OrderId = item.Id,
                    Message = $"В заказе {item.Id} для работы «{line.Name}» не указана ставка НДС."
                })));
        if (options.DestinationCode == "bp3")
        {
            package.ValidationIssues.AddRange(package.Orders
                .Where(item => !item.IsPaymentContextOnly)
                .SelectMany(item => item.Lines
                    .Where(line => line.DiscountedTotalPrice > 0m
                        && line.Quantity > line.DiscountedTotalPrice * 100m)
                    .Select(line => new Export1CPackageDto.ValidationIssue
                    {
                        EntityType = "order_line",
                        EntityId = line.Id,
                        OrderId = item.Id,
                        Message = $"Для работы «{line.Name}» сумма после скидки меньше копейки на единицу. Загрузчик 1С не сможет сохранить количество и сумму без нулевой цены."
                    })));
        }

        if (options.IncludeClients)
        {
            package.Clients = await this.LoadClientsAsync(organizationId);
        }

        if (options.IncludeVehicles)
        {
            package.Vehicles = await this.LoadVehiclesAsync(organizationId);
        }

        if (options.IncludeCatalog)
        {
            package.CatalogItems = await this.LoadCatalogAsync(organizationId);
        }

        if (options.IncludeMaterials)
        {
            package.Stocks = await this.LoadStocksAsync(organizationId);
            package.MaterialMovements = await this.LoadMaterialMovementsAsync(
                organizationId,
                recordedFromUtc,
                recordedToUtc);
        }

        return package;
    }

    /// <summary> Загружает платежи организации за период. </summary>
    private async Task<List<Export1CPackageDto.PaymentInfo>> LoadPaymentsAsync(
        Guid organizationId,
        DateTime fromUtc,
        DateTime toUtc)
    {
        return await opencrmContext.MaintenancePayments
            .AsNoTracking()
            .Where(item => item.Maintenance.Vehicle.OrganizationId == organizationId)
            .Where(item => item.CreatedAt >= fromUtc && item.CreatedAt < toUtc
                || item.CancelledAt >= fromUtc && item.CancelledAt < toUtc)
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .Select(item => new Export1CPackageDto.PaymentInfo
            {
                Id = item.Id,
                OrderId = item.MaintenanceId,
                Amount = item.Amount,
                MethodCode = item.MethodCode,
                PayerClientId = item.PayerClientId,
                PayerName = item.PayerName,
                PayerPartyTypeCode = item.PayerPartyTypeCode,
                PayerInn = item.PayerInn,
                PayerKpp = item.PayerKpp,
                PayerLegalAddress = item.PayerLegalAddress,
                RecordedAtUtc = item.CreatedAt,
                CreatedBy = item.CreatedBy,
                IsOpeningBalance = item.IsOpeningBalance,
                CancelledAtUtc = item.CancelledAt,
                CancelledBy = item.CancelledBy,
                CancellationReason = item.CancellationReason,
                Comment = item.Comment
            })
            .ToListAsync();
    }

    /// <summary> Загружает заказы организации за период. </summary>
    private async Task<List<Export1CPackageDto.OrderInfo>> LoadOrdersAsync(
        Guid organizationId,
        Export1COptionsDto options,
        DateTime orderFrom,
        DateTime orderToExclusive,
        IReadOnlyCollection<Export1CPackageDto.PaymentInfo> payments)
    {
        var paymentOrderIds = payments.Select(item => item.OrderId).Distinct().ToArray();
        var orders = await opencrmContext.Maintenance
            .AsNoTracking()
            .Where(item => item.Vehicle.OrganizationId == organizationId)
            .Where(item => paymentOrderIds.Contains(item.Id)
                || options.IncludeOrders && !item.IsDeleted
                && (options.IncludeUnfinishedOrders || item.Status == MaintenanceStatus.Completed)
                && (item.CompletedDate ?? item.Date) >= orderFrom
                && (item.CompletedDate ?? item.Date) < orderToExclusive)
            .Include(item => item.Vehicle)
                .ThenInclude(vehicle => vehicle.Brand)
            .Include(item => item.Vehicle)
                .ThenInclude(vehicle => vehicle.Model)
            .OrderBy(item => item.CompletedDate ?? item.Date)
            .ThenBy(item => item.Id)
            .ToListAsync();

        return orders.Select(item =>
        {
            var lines = (item.Details ?? [])
                .Where(line => line.Price.HasValue || line.MaterialCatalogItemId == null
                    && string.IsNullOrWhiteSpace(line.MaterialName) && line.MaterialQuantity == null)
                .ToList();
            var grossAmount = Math.Round(
                lines.Sum(line => (decimal)(line.Price ?? 0)),
                2,
                MidpointRounding.AwayFromZero);
            var isInRequestedPeriod = options.IncludeOrders && !item.IsDeleted
                && (options.IncludeUnfinishedOrders || item.Status == MaintenanceStatus.Completed)
                && (item.CompletedDate ?? item.Date) >= orderFrom
                && (item.CompletedDate ?? item.Date) < orderToExclusive;
            var vehicle = item.Vehicle;

            return new Export1CPackageDto.OrderInfo
            {
                Id = item.Id,
                IsPaymentContextOnly = !isInRequestedPeriod,
                VehicleId = item.VehicleId,
                ClientId = item.BuyerClientId,
                ClientName = item.BuyerClientId.HasValue ? item.BuyerName : null,
                ClientPartyTypeCode = item.BuyerClientId.HasValue ? item.BuyerPartyTypeCode : null,
                ClientInn = item.BuyerClientId.HasValue ? item.BuyerInn : null,
                ClientKpp = item.BuyerClientId.HasValue ? item.BuyerKpp : null,
                ClientLegalAddress = item.BuyerClientId.HasValue ? item.BuyerLegalAddress : null,
                VehicleName = vehicle.Name,
                RegistrationPlate = vehicle.RegistrationPlate,
                WorkActNumber = item.WorkActNumber,
                IsDeleted = item.IsDeleted,
                Status = item.Status.ToString(),
                Date = item.Date,
                PlannedDate = item.PlannedDate,
                CompletedDate = item.CompletedDate,
                HandedOverAt = item.HandedOverAt,
                GrossAmount = grossAmount,
                DiscountAmount = item.DiscountAmount,
                NetAmount = Math.Max(0m, grossAmount - item.DiscountAmount),
                PaidAmount = item.PaidAmount,
                MaterialCostAmount = item.MaterialCostAmount,
                LaborCostAmount = item.LaborCostAmount,
                Lines = BuildOrderLines(lines, grossAmount, item.DiscountAmount)
            };
        }).ToList();
    }

    /// <summary> Строит позиции заказа из строк обслуживания с распределением сумм. </summary>
    private static List<Export1CPackageDto.OrderLineInfo> BuildOrderLines(
        IReadOnlyList<MaintenanceDetail> details,
        decimal grossAmount,
        decimal discountAmount)
    {
        var result = new List<Export1CPackageDto.OrderLineInfo>(details.Count);
        var netAmount = Math.Max(0m, grossAmount - discountAmount);
        var remaining = netAmount;
        var lastPricedIndex = -1;
        for (var index = 0; index < details.Count; index++)
        {
            if (details[index].Price.HasValue)
            {
                lastPricedIndex = index;
            }
        }

        for (var index = 0; index < details.Count; index++)
        {
            var detail = details[index];
            var totalPrice = detail.Price.HasValue
                ? Math.Round((decimal)detail.Price.Value, 2, MidpointRounding.AwayFromZero)
                : (decimal?)null;
            var discountedTotalPrice = !totalPrice.HasValue
                ? 0m
                : index == lastPricedIndex
                ? remaining
                : grossAmount == 0m
                    ? 0m
                    : Math.Min(remaining, Math.Round(netAmount * totalPrice.Value / grossAmount, 2,
                        MidpointRounding.AwayFromZero));
            remaining -= discountedTotalPrice;
            var vatPercent = MaintenanceVatRates.GetPercent(detail.VatRateCode);

            result.Add(new Export1CPackageDto.OrderLineInfo
            {
                Id = detail.DetailId,
                CatalogItemId = detail.CatalogItemId,
                Name = detail.Name,
                Description = detail.Description,
                Quantity = detail.Quantity,
                TotalPrice = totalPrice,
                DiscountedTotalPrice = discountedTotalPrice,
                VatRateCode = detail.VatRateCode,
                VatAmount = !totalPrice.HasValue
                    ? null
                    : detail.VatRateCode == MaintenanceVatRates.WithoutVat
                    ? 0m
                    : vatPercent.HasValue
                        ? Math.Round(discountedTotalPrice * vatPercent.Value / (100m + vatPercent.Value), 2,
                            MidpointRounding.AwayFromZero)
                        : null,
                MaterialCatalogItemId = detail.MaterialCatalogItemId,
                MaterialName = detail.MaterialName,
                MaterialQuantity = detail.MaterialQuantity,
                MaterialUnit = detail.MaterialUnit,
                MaterialUnitCost = detail.MaterialUnitCost,
                MaterialTotalCost = detail.MaterialTotalCost
            });
        }

        return result;
    }

    /// <summary> Загружает клиентов организации. </summary>
    private async Task<List<Export1CPackageDto.ClientInfo>> LoadClientsAsync(Guid organizationId)
    {
        return await opencrmContext.OrganizationClients
            .AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && !item.IsDeleted)
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Id)
            .Select(item => new Export1CPackageDto.ClientInfo
            {
                Id = item.Id,
                Name = item.Name,
                Phone = item.Phone,
                PartyTypeCode = item.PartyTypeCode,
                Inn = item.Inn,
                Kpp = item.Kpp,
                LegalAddress = item.LegalAddress,
                StatusCode = item.StatusCode
            })
            .ToListAsync();
    }

    /// <summary> Загружает технику организации. </summary>
    private async Task<List<Export1CPackageDto.VehicleInfo>> LoadVehiclesAsync(Guid organizationId)
    {
        return await opencrmContext.Vehicles
            .AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && !item.IsDeleted)
            .OrderBy(item => item.Id)
            .Select(item => new Export1CPackageDto.VehicleInfo
            {
                Id = item.Id,
                ClientId = item.OrganizationClientId,
                Name = item.Name,
                Brand = item.Brand != null ? item.Brand.Name : item.ManualBrandName,
                Model = item.Model != null ? item.Model.Name : item.ManualModelName,
                Vin = item.Vin,
                RegistrationPlate = item.RegistrationPlate,
                IsArchived = item.IsArchived
            })
            .ToListAsync();
    }

    /// <summary> Загружает записи каталога организации. </summary>
    private async Task<List<Export1CPackageDto.CatalogItemInfo>> LoadCatalogAsync(Guid organizationId)
    {
        return await opencrmContext.OrganizationScenarioCatalogItems
            .AsNoTracking()
            .Where(item => item.OrganizationId == organizationId
                && (item.KindCode == OrganizationScenarioCatalogKindCodes.Service
                    || item.KindCode == OrganizationScenarioCatalogKindCodes.ServiceBundle
                    || item.KindCode == OrganizationScenarioCatalogKindCodes.Material
                    || item.KindCode == OrganizationScenarioCatalogKindCodes.WorkType))
            .OrderBy(item => item.KindCode)
            .ThenBy(item => item.Code)
            .Select(item => new Export1CPackageDto.CatalogItemInfo
            {
                Id = item.Id,
                ScenarioCode = item.ScenarioCode,
                KindCode = item.KindCode,
                Code = item.Code,
                Name = item.Name,
                Description = item.Description,
                UnitPrice = item.UnitPrice,
                ServiceIds = item.BundleItems.OrderBy(value => value.Position).Select(value => value.ServiceId).ToArray(),
                IsActive = item.IsActive
            })
            .ToListAsync();
    }

    /// <summary> Загружает остатки организации. </summary>
    private async Task<List<Export1CPackageDto.StockInfo>> LoadStocksAsync(Guid organizationId)
    {
        return await opencrmContext.OrganizationMaterialStocks
            .AsNoTracking()
            .Where(item => item.OrganizationId == organizationId)
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Id)
            .Select(item => new Export1CPackageDto.StockInfo
            {
                Id = item.Id,
                CatalogItemId = item.CatalogItemId,
                Name = item.Name,
                Unit = item.Unit,
                PackageQuantity = item.PackageQuantity,
                QuantityOnHand = item.QuantityOnHand,
                ReservedQuantity = item.ReservedQuantity,
                AverageUnitCost = item.AverageUnitCost,
                InventoryValue = item.InventoryValue
            })
            .ToListAsync();
    }

    /// <summary> Загружает движения материалов организации за период. </summary>
    private async Task<List<Export1CPackageDto.MaterialMovementInfo>> LoadMaterialMovementsAsync(
        Guid organizationId,
        DateTime fromUtc,
        DateTime toUtc)
    {
        return await opencrmContext.OrganizationMaterialMovements
            .AsNoTracking()
            .Where(item => item.OrganizationId == organizationId
                && item.CreatedAt >= fromUtc && item.CreatedAt < toUtc)
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .Select(item => new Export1CPackageDto.MaterialMovementInfo
            {
                Id = item.Id,
                StockId = item.StockId,
                OrderId = item.MaintenanceId,
                OperationId = item.OperationId,
                TypeCode = item.TypeCode,
                RecordedAtUtc = item.CreatedAt,
                Quantity = item.Quantity,
                EnteredQuantity = item.EnteredQuantity,
                EnteredUnit = item.EnteredUnit,
                ConversionFactor = item.ConversionFactor,
                Unit = item.Unit,
                UnitCost = item.UnitCost,
                TotalCost = item.TotalCost,
                QuantityBalanceAfter = item.QuantityBalanceAfter,
                CostBalanceAfter = item.CostBalanceAfter,
                SourceEntityType = item.SourceEntityType,
                SourceEntityId = item.SourceEntityId,
                Comment = item.Comment
            })
            .ToListAsync();
    }
}
