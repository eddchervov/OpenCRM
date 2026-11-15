using Microsoft.Extensions.Configuration;
using Notifications.Infrastructure.Templating;

namespace Notifications.Tests.Unit;

/// <summary>Проверяет перенос общего названия проекта OpenCRM в настройки бренда писем.</summary>
public sealed class SystemTemplateBrandConfigurationTests
{
    /// <summary>Общее название заполняет только незаданные настройки бренда и подписи отправителя.</summary>
    [Fact]
    public void ProjectNameFillsMissingBrandSettings()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["SystemTemplates:Applications:opencrm:BrandName"] = "Своё имя",
            ["SystemTemplates:Applications:catalog:BrandMark"] = "Catalog mark"
        });

        SystemTemplateBrandConfiguration.ApplyProjectBrand(configuration, "СТО «Пример»");

        Assert.Equal("Своё имя", configuration["SystemTemplates:Applications:opencrm:BrandName"]);
        Assert.Equal("С", configuration["SystemTemplates:Applications:opencrm:BrandMark"]);
        Assert.Equal("СТО «Пример»", configuration["SystemTemplates:Applications:catalog:BrandName"]);
        Assert.Equal("Catalog mark", configuration["SystemTemplates:Applications:catalog:BrandMark"]);
        Assert.Equal("СТО «Пример»", configuration["Email:FromName"]);
        Assert.Equal("СТО «Пример»", configuration["Smtp:FromName"]);
    }

    /// <summary>Явные переопределения окружения и приложения важнее общего названия проекта.</summary>
    [Fact]
    public void ExplicitOverridesWinOverProjectDefaults()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["Smtp:FromName"] = "СТО «Пример» — рассылка",
            ["Email:FromName"] = "СТО «Пример» — служебные",
            ["SystemTemplates:Applications:opencrm:BrandName"] = "СТО «Пример»",
            ["SystemTemplates:Applications:opencrm:BrandMark"] = "П"
        });

        SystemTemplateBrandConfiguration.ApplyProjectBrand(configuration, "OpenCRM");

        Assert.Equal("СТО «Пример» — рассылка", configuration["Smtp:FromName"]);
        Assert.Equal("СТО «Пример» — служебные", configuration["Email:FromName"]);
        Assert.Equal("СТО «Пример»", configuration["SystemTemplates:Applications:opencrm:BrandName"]);
        Assert.Equal("П", configuration["SystemTemplates:Applications:opencrm:BrandMark"]);
    }

    /// <summary>Метка берётся из названия, а без букв в названии ключ метки не создаётся.</summary>
    [Theory]
    [InlineData("open crm", "O")]
    [InlineData("ёлка", "Ё")]
    [InlineData("𝕏-сервис", "𝕏")]
    [InlineData("123", null)]
    public void MarkComesFromTheProjectNameLetter(string projectName, string? expectedMark)
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["SystemTemplates:Applications:opencrm:Templates:0"] = "system/login-code"
        });

        SystemTemplateBrandConfiguration.ApplyProjectBrand(configuration, projectName);

        Assert.Equal(projectName.Trim(), configuration["SystemTemplates:Applications:opencrm:BrandName"]);
        Assert.Equal(expectedMark, configuration["SystemTemplates:Applications:opencrm:BrandMark"]);
    }

    /// <summary>Метка профиля из одних пробелов заменяется производной от названия.</summary>
    [Fact]
    public void WhitespaceMarkIsReplacedByDerivedLetter()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["SystemTemplates:Applications:opencrm:BrandMark"] = "   "
        });

        SystemTemplateBrandConfiguration.ApplyProjectBrand(configuration, "СТО «Пример»");

        Assert.Equal("С", configuration["SystemTemplates:Applications:opencrm:BrandMark"]);
    }

    /// <summary>Профили без настройки не создаются, а пустое название отклоняется.</summary>
    [Fact]
    public void MissingProfilesAreNotCreatedAndEmptyNameIsRejected()
    {
        var configuration = Build([]);

        SystemTemplateBrandConfiguration.ApplyProjectBrand(configuration, "  Пример  ");

        Assert.Null(configuration["SystemTemplates:Applications:opencrm:BrandName"]);
        Assert.Equal("Пример", configuration["Email:FromName"]);
        Assert.Throws<ArgumentException>(() => SystemTemplateBrandConfiguration.ApplyProjectBrand(configuration, "   "));
    }

    private static IConfigurationRoot Build(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
