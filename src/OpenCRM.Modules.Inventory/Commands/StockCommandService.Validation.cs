using OpenCRM.Modules.Inventory.Contracts.Dtos;

namespace OpenCRM.Modules.Inventory.Commands;

internal sealed partial class StockCommandService
{
    private const int MaxMaterialNameLength = 160;
    private const int MaxManualCommentLength = 1000;
    private const decimal MaxUnitCost = 999_999_999;

    private static void ValidateReceipt(CreateOrganizationMaterialReceiptDto request)
    {
        ValidateQuantity(request.Quantity, "Количество прихода", allowZero: false);
        ValidatePackageQuantity(request.PackageQuantity);
        ValidateCost(request.UnitCost, "Себестоимость прихода", 4);
        ValidateComment(request.Comment, required: false);
    }

    private static void ValidateIssue(CreateOrganizationMaterialIssueDto request)
    {
        ValidateQuantity(request.Quantity, "Количество списания", allowZero: false);
        ValidateComment(request.Comment, required: false);
    }

    private static void ValidateAdjustment(AdjustOrganizationMaterialStockDto request)
    {
        if (!request.ExpectedUpdatedAt.HasValue)
        {
            throw new InvalidOperationException("Обновите остаток и повторите корректировку.");
        }

        ValidateQuantity(request.QuantityOnHand, "Остаток", allowZero: true);
        ValidateCost(request.AverageUnitCost, "Себестоимость", 8);
        ValidateComment(request.Comment, required: true, maximumLength: 800);
    }

    private static void ValidateMaterialName(string name)
    {
        if (name.Length > MaxMaterialNameLength)
        {
            throw new InvalidOperationException("Название материала не должно превышать 160 символов.");
        }
    }

    private static void ValidateQuantity(decimal quantity, string fieldName, bool allowZero)
    {
        if (quantity < 0 || (!allowZero && quantity == 0))
        {
            throw new InvalidOperationException(allowZero
                ? $"{fieldName} не может быть отрицательным."
                : $"{fieldName} должно быть больше нуля.");
        }
        if (quantity > MaxQuantity)
        {
            throw new InvalidOperationException($"{fieldName} не может превышать 999999999.");
        }
        if (quantity != decimal.Round(quantity, 3))
        {
            throw new InvalidOperationException($"{fieldName} может содержать не более трех знаков после запятой.");
        }
    }

    private static void ValidateCost(decimal? cost, string fieldName, int decimalPlaces)
    {
        if (!cost.HasValue)
        {
            return;
        }
        if (cost.Value < 0 || cost.Value > MaxUnitCost)
        {
            throw new InvalidOperationException($"{fieldName} должна быть от 0 до 999999999.");
        }
        if (cost.Value != decimal.Round(cost.Value, decimalPlaces))
        {
            throw new InvalidOperationException($"{fieldName} может содержать не более {decimalPlaces} знаков после запятой.");
        }
    }

    private static void ValidateComment(string? value, bool required, int maximumLength = MaxManualCommentLength)
    {
        var comment = value?.Trim();
        if (required && string.IsNullOrWhiteSpace(comment))
        {
            throw new InvalidOperationException("Укажите причину корректировки склада.");
        }
        if (comment?.Length > maximumLength)
        {
            throw new InvalidOperationException(required
                ? $"Причина корректировки не должна превышать {maximumLength} символов."
                : $"Комментарий не должен превышать {maximumLength} символов.");
        }
    }
}
