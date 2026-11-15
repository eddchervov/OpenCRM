using OpenCRM.Modules.Maintenance;

namespace OpenCRM.Modules.Maintenance.Tests.Unit;

public sealed class MaintenanceMoneyAndTextRulesTests
{
    public static TheoryData<decimal?, decimal> MoneyCases => new()
    {
        { null, 0m },
        { -4.125m, 0m },
        { 1.125m, 1.13m },
        { 1.124m, 1.12m }
    };

    [Theory]
    [MemberData(nameof(MoneyCases))]
    public void NormalizeMoneyClampsAndRoundsAwayFromZero(decimal? value, decimal expected)
    {
        Assert.Equal(expected, MaintenanceMoneyRules.NormalizeMoney(value));
    }

    [Fact]
    public void ApplyFinancialFieldsNormalizesEveryAmount()
    {
        var maintenance = new MaintenanceEntity();

        MaintenanceMoneyRules.ApplyFinancialFields(maintenance, -1, 12.345m, null);

        Assert.Equal(0, maintenance.DiscountAmount);
        Assert.Equal(12.35m, maintenance.PaidAmount);
        Assert.Equal(0, maintenance.LaborCostAmount);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    [InlineData("Пост № 1", "Пост № 1")]
    [InlineData("Длинная строка", "Длинная ")]
    public void TruncateReturnsShortInputOrCutsAtTheRequestedLength(string? value, string? expected)
    {
        var limit = expected?.Length ?? 0;

        Assert.Equal(expected, MaintenanceTextRules.Truncate(value, limit));
    }

    [Fact]
    public void TruncateCutsLongInputWithoutChangingShortInput()
    {
        Assert.Equal("пост", MaintenanceTextRules.Truncate("пост 1", 4));
        Assert.Equal("пост 1", MaintenanceTextRules.Truncate("пост 1", 6));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeOptionalReturnsNullForEmptyValues(string? value)
    {
        Assert.Null(MaintenanceTextRules.NormalizeOptional(value));
    }

    [Fact]
    public void NormalizeOptionalTrimsAndCollapsesWhitespace()
    {
        Assert.Equal("Первый пост", MaintenanceTextRules.NormalizeOptional("  Первый\t\r\n  пост "));
    }

    [Theory]
    [InlineData("<b>работа</b>")]
    [InlineData("работа > осмотр")]
    [InlineData("JAVASCRIPT:alert(1)")]
    public void NormalizeOptionalRejectsMarkupAndScripts(string value)
    {
        Assert.Throws<InvalidOperationException>(() => MaintenanceTextRules.NormalizeOptional(value));
    }

    [Fact]
    public void ValidateSafeTextAllowsEmptyTextAndRejectsExcessiveLength()
    {
        MaintenanceTextRules.ValidateSafeText("Комментарий", null);
        MaintenanceTextRules.ValidateSafeText("Комментарий", "   ");
        MaintenanceTextRules.ValidateSafeText("Комментарий", new string('а', 1000));

        var exception = Assert.Throws<InvalidOperationException>(() => MaintenanceTextRules.ValidateSafeText(
            "Комментарий", new string('а', 1001)));

        Assert.Contains("максимум 1000 символов", exception.Message);
    }
}
