using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Modules.Inventory.Contracts.Dtos;
using OpenCRM.WebApp.Infrastructure.AiPilot;
using OpenCRM.WebApp.Infrastructure.Assistant;

namespace OpenCRM.WebApp.Infrastructure.Assistant.Actions;

public sealed class OperationsAssistantInventoryActionHandler(
    IInventoryQueryService inventoryQuery,
    IStockCommandService stockCommand,
    ICatalogQueryService catalogQuery,
    IAssistantExecutionGuard executionGuard,
    AssistantOptions options) : IAssistantActionHandler
{
    public IReadOnlyCollection<string> Actions { get; } = ["inventory_list", "inventory_receive", "inventory_adjust", "inventory_issue"];

    public async Task<AssistantPreparedAction> PrepareAsync(
        AiPilotCommand command,
        AssistantActor actor,
        AssistantActionSelection selection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parameters = new OperationsAssistantParameters(command);
        var inventory = await inventoryQuery.GetAsync(actor.UserId, actor.OrganizationId, null, null, null, 1,
            OperationsAssistantLimits.PageSize(options), 1, 1);
        var stocks = inventory.Stocks;
        if (command.Action == "inventory_list")
        {
            var name = parameters.OptionalText("name");
            var selected = (name is null
                ? stocks
                : stocks.Where(stock => string.Equals(stock.Name.Trim(), name.Trim(), StringComparison.CurrentCultureIgnoreCase))).ToArray();
            if (TryGetSelection(selection, "name", out var selectedStockId))
            {
                selected = selected.Where(stock => stock.Id.ToString() == selectedStockId).ToArray();
            }

            if (name is not null && selected.Length != 1)
            {
                throw new InvalidOperationException(selected.Length == 0 ? "Материал не найден на складе." : "Найдено несколько материалов с таким названием.");
            }

            return ReadOnly("Склад", selected.Select(stock => $"{stock.Name}: остаток {stock.QuantityOnHand} {stock.Unit}").ToArray(), new { Inventory = inventory, Selected = selected });
        }

        var materialName = parameters.RequiredText("name", 150);
        var matches = stocks.Where(stock => string.Equals(stock.Name.Trim(), materialName.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToArray();
        if (TryGetSelection(selection, "name", out var chosenStockId))
        {
            matches = matches.Where(stock => stock.Id.ToString() == chosenStockId).ToArray();
        }

        matches = OperationsAssistantLimits.Candidates(options, matches);
        if (matches.Length > 1)
        {
            throw new AssistantClarificationException("Найдено несколько материалов. Уточните складскую позицию.", "name", OperationsAssistantLimits.Choices(options, matches.Select(item => new AssistantChoice(item.Id.ToString(), item.Name))));
        }

        var stock = matches.SingleOrDefault();
        if (command.Action == "inventory_receive")
        {
            var catalog = await catalogQuery.GetAsync(actor.UserId, actor.OrganizationId);
            var unit = NormalizeUnit(parameters.OptionalText("unit") ?? stock?.Unit ?? "шт");
            var request = new CreateOrganizationMaterialReceiptDto
            {
                Name = stock?.Name ?? materialName,
                CatalogItemId = stock?.CatalogItemId ?? FindCatalogMaterial(catalog.Materials, materialName)?.Id,
                Unit = unit,
                Quantity = parameters.RequiredPositiveDecimal("quantity"),
                PackageQuantity = parameters.OptionalDecimal("packageQuantity") ?? stock?.PackageQuantity,
                UnitCost = parameters.OptionalDecimal("unitCost"),
                Comment = parameters.OptionalText("comment")
            };
            return Mutation($"Склад: {request.Name}", [$"Принять {request.Quantity} {request.Unit}"], new { Inventory = inventory, Catalog = catalog }, async ct =>
            {
                await executionGuard.EnsureAllowedAsync(actor, command.Action, ct);
                await stockCommand.AddReceiptAsync(actor.UserId, actor.OrganizationId, request);
                return new AssistantActionResult($"На склад принято {request.Quantity} {request.Unit} материала «{request.Name}».");
            });
        }

        if (stock is null)
        {
            throw new InvalidOperationException($"Материал «{materialName}» не найден на складе.");
        }

        var requestedUnit = NormalizeUnit(parameters.OptionalText("unit") ?? stock.Unit);
        if (command.Action == "inventory_issue")
        {
            var quantity = ConvertQuantityForOperation(parameters.RequiredPositiveDecimal("quantity"), requestedUnit, stock.Unit, stock.PackageQuantity);
            if (quantity > stock.AvailableQuantity)
            {
                throw new InvalidOperationException($"Доступно только {stock.AvailableQuantity} {stock.Unit}.");
            }

            var request = new CreateOrganizationMaterialIssueDto { StockId = stock.Id, Quantity = quantity, Comment = parameters.OptionalText("comment") };
            return Mutation(stock.Name, [$"Списать {quantity} {stock.Unit}"], new { Inventory = inventory, Stock = stock }, async ct =>
            {
                await executionGuard.EnsureAllowedAsync(actor, command.Action, ct);
                await stockCommand.IssueAsync(actor.UserId, actor.OrganizationId, request);
                return new AssistantActionResult($"Списано {quantity} {stock.Unit} материала «{stock.Name}».");
            }, destructive: true);
        }

        var newQuantity = ConvertQuantityForOperation(parameters.RequiredNonnegativeDecimalEither("quantityOnHand", "quantity"), requestedUnit, stock.Unit, stock.PackageQuantity);
        var unitCost = parameters.OptionalDecimal("unitCost");
        var conversionFactor = ConvertQuantityForOperation(1m, requestedUnit, stock.Unit, stock.PackageQuantity);
        var averageUnitCost = unitCost is null ? stock.AverageUnitCost : unitCost.Value / conversionFactor;
        var reason = parameters.RequiredText("comment", 500);
        var adjustment = new AdjustOrganizationMaterialStockDto
        {
            ExpectedUpdatedAt = stock.UpdatedAt,
            QuantityOnHand = newQuantity,
            AverageUnitCost = averageUnitCost,
            Comment = reason
        };
        return Mutation(stock.Name, [$"Установить остаток {newQuantity} {stock.Unit}", $"Причина: {reason}"], new { Inventory = inventory, Stock = stock }, async ct =>
        {
            await executionGuard.EnsureAllowedAsync(actor, command.Action, ct);
            await stockCommand.AdjustAsync(actor.UserId, actor.OrganizationId, stock.Id, adjustment);
            return new AssistantActionResult($"Остаток «{stock.Name}» скорректирован.");
        }, destructive: true);
    }

    private static string NormalizeUnit(string value)
    {
        var unit = value.Trim().ToLowerInvariant();
        return unit switch
        {
            "шт." or "штук" or "штука" => "шт",
            "литр" or "литра" or "литров" => "л",
            "миллилитр" or "миллилитров" => "мл",
            "килограмм" or "килограммов" => "кг",
            "грамм" or "граммов" => "г",
            "упаковка" or "упаковок" => "уп",
            "шт" or "л" or "мл" or "кг" or "г" or "м" or "м2" or "компл" or "уп" => unit,
            _ => throw new InvalidOperationException("Назовите единицу материала: шт, л, мл, кг, г, м, м2, компл или уп.")
        };
    }

    internal static decimal ConvertQuantityForOperation(decimal quantity, string requestedUnit, string storedUnit, decimal? packageQuantity)
    {
        var requested = NormalizeUnit(requestedUnit);
        var stored = NormalizeUnit(storedUnit);
        var factor = requested switch
        {
            "уп" when packageQuantity is > 0 => packageQuantity.Value,
            "уп" => throw new InvalidOperationException("Для материала не указан размер упаковки."),
            _ when requested == stored => 1m,
            "л" when stored == "мл" => 1000m,
            "кг" when stored == "г" => 1000m,
            "мл" when stored == "л" => 0.001m,
            "г" when stored == "кг" => 0.001m,
            _ => throw new InvalidOperationException($"Единица {requested} не соответствует складской единице {stored}.")
        };

        var converted = quantity * factor;
        if (decimal.Round(converted, 3) != converted)
        {
            throw new InvalidOperationException("После перевода единиц допустимы три знака после запятой.");
        }

        return converted;
    }

    private OpenCRM.Modules.Catalog.Contracts.Dtos.OrganizationScenarioCatalogItemDto? FindCatalogMaterial(
        IEnumerable<OpenCRM.Modules.Catalog.Contracts.Dtos.OrganizationScenarioCatalogItemDto> materials,
        string name)
    {
        var matches = OperationsAssistantLimits.Candidates(options, materials.Where(item => string.Equals(item.Name.Trim(), name.Trim(), StringComparison.CurrentCultureIgnoreCase)));
        if (matches.Length > 1)
        {
            throw new AssistantClarificationException("Найдено несколько материалов в справочнике. Уточните название.", "name", OperationsAssistantLimits.Choices(options, matches.Select(item => new AssistantChoice(item.Id.ToString(), item.Name))));
        }

        return matches.SingleOrDefault();
    }

    private static AssistantPreparedAction ReadOnly(string target, IReadOnlyList<string> changes, object snapshot)
    {
        return new AssistantPreparedAction
        {
            Target = target,
            Changes = changes,
            Snapshot = Fingerprint(snapshot),
            ReadOnly = true,
            Data = JsonSerializer.SerializeToElement(snapshot),
            ExecuteAsync = _ => Task.FromResult(new AssistantActionResult("Данные показаны."))
        };
    }

    private static AssistantPreparedAction Mutation(string target, IReadOnlyList<string> changes, object snapshot, Func<CancellationToken, Task<AssistantActionResult>> execute, bool destructive = false)
    {
        return new AssistantPreparedAction
        {
            Target = target,
            Changes = changes,
            Snapshot = Fingerprint(snapshot),
            Destructive = destructive,
            ExecuteAsync = execute
        };
    }

    private static string Fingerprint(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));

    private static bool TryGetSelection(AssistantActionSelection selection, string field, out string value)
    {
        if (selection.CatalogChoices is not null && selection.CatalogChoices.TryGetValue(field, out value!))
        {
            return true;
        }

        value = string.Empty;
        return false;
    }
}
