namespace OpenCRM.Modules.Maintenance.Contracts.Imports;

/// <summary>Стабильные коды причин проверки бизнес-строк импорта.</summary>
public static class ImportBusinessMessageCodes
{
    public const string DuplicateTargetId = "duplicateTargetId";
    public const string OrderExternalIdRequired = "orderExternalIdRequired";
    public const string DuplicateOrderExternalId = "duplicateOrderExternalId";
    public const string OrderCommentTooLong = "orderCommentTooLong";
    public const string VehicleReferenceNotFound = "vehicleReferenceNotFound";
    public const string OrderDateInvalid = "orderDateInvalid";
    public const string OrderStatusInvalid = "orderStatusInvalid";
    public const string OrderTypeInvalid = "orderTypeInvalid";
    public const string DiscountInvalid = "discountInvalid";
    public const string TooManyOrderChildren = "tooManyOrderChildren";
    public const string TooManyOrderChildrenOnChild = "tooManyOrderChildrenOnChild";
    public const string OrderLineRequired = "orderLineRequired";
    public const string OrderLineNameRequired = "orderLineNameRequired";
    public const string OrderLineQuantityInvalid = "orderLineQuantityInvalid";
    public const string OrderLineUnitPriceInvalid = "orderLineUnitPriceInvalid";
    public const string OrderLineAmountOutOfRange = "orderLineAmountOutOfRange";
    public const string OrderLineTotalOutOfRange = "orderLineTotalOutOfRange";
    public const string PaymentAmountInvalid = "paymentAmountInvalid";
    public const string PaymentDateInvalid = "paymentDateInvalid";
    public const string PaymentMethodInvalid = "paymentMethodInvalid";
    public const string PaymentMetadataTooLong = "paymentMetadataTooLong";
    public const string PaymentCancellationDateInvalid = "paymentCancellationDateInvalid";
    public const string PaymentTotalOutOfRange = "paymentTotalOutOfRange";
    public const string HistoryDateInvalid = "historyDateInvalid";
    public const string HistoryTypeInvalid = "historyTypeInvalid";
    public const string HistoryAuthorTooLong = "historyAuthorTooLong";
    public const string DiscountExceedsOrderLines = "discountExceedsOrderLines";
    public const string OrderAmountMismatch = "orderAmountMismatch";
    public const string PaymentsExceedOrderAmount = "paymentsExceedOrderAmount";
    public const string CancelledOrderHasActivePayments = "cancelledOrderHasActivePayments";
    public const string ChildOrderExternalIdRequired = "childOrderExternalIdRequired";
    public const string ChildOrderNotFound = "childOrderNotFound";
    public const string PartyNameTooLong = "partyNameTooLong";
    public const string PartySnapshotRequired = "partySnapshotRequired";
    public const string PartyClientNotFound = "partyClientNotFound";
}
