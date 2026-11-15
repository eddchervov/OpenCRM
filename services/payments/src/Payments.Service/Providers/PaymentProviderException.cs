namespace Payments.Service.Providers;

/// <summary> Различает доказанный отказ и неизвестный исход внешнего запроса. </summary>
public sealed class PaymentProviderException(string code, bool isDefinitive = false, bool requiresManualReview = false)
    : Exception(code)
{
    public string Code { get; } = code;
    public bool IsDefinitive { get; } = isDefinitive;
    public bool RequiresManualReview { get; } = requiresManualReview;
}
