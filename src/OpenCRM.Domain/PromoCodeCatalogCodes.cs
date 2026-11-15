namespace OpenCRM.Domain;

/// <summary> Коды справочника промокодов. </summary>
public static class PromoCodeCatalogCodes
{
    /// <summary> Коды типов скидки промокода. </summary>
    public static class DiscountTypes
    {
        /// <summary> Тип: скидка отсутствует. </summary>
        public const string None = "none";

        /// <summary> Тип: скидка в процентах. </summary>
        public const string Percentage = "percentage";

        /// <summary> Тип: скидка фиксированной суммой. </summary>
        public const string FixedAmount = "fixed_amount";

        /// <summary> Тип: фиксированная цена после скидки. </summary>
        public const string FixedPrice = "fixed_price";

        /// <summary> Тип: бонусные дни. </summary>
        public const string BonusDays = "bonus_days";

        /// <summary> Множество всех кодов типов скидки. </summary>
        public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            None,
            Percentage,
            FixedAmount,
            FixedPrice,
            BonusDays
        };
    }
}
