using OpenCRM.Services.Interfaces.Features.Dtos;

namespace OpenCRM.WebApp.Models.Features;

/// <summary> Модель отображения набора характеристик на странице. </summary>
public class FeatureCollectionViewModel
{
    /// <summary> Список характеристик для отображения. </summary>
    public IEnumerable<FeatureValueDto>? Features { get; set; }

    /// <summary> Отображать характеристики в виде бейджей. </summary>
    public bool RenderAsBadges { get; set; } = true;

    /// <summary> Показывать описания характеристик. </summary>
    public bool ShowDescriptions { get; set; }

    /// <summary> Показывать преимущества продукта. </summary>
    public bool ShowProductHighlights { get; set; }

    /// <summary> Показывать преимущества для бизнес-аудитории. </summary>
    public bool ProductHighlightsForBusiness { get; set; }

    /// <summary> Показывать ограничение по количеству транспортных средств. </summary>
    public bool ShowVehiclesLimit { get; set; }

    /// <summary> Ограничение по количеству транспортных средств, если характеристика не задана. </summary>
    public int? FallbackVehicleLimit { get; set; }

    /// <summary> CSS-класс бейджа. </summary>
    public string BadgeCssClass { get; set; } = "bg-light text-dark border";

    /// <summary> CSS-класс иконки. </summary>
    public string IconCssClass { get; set; } = "fa fa-check text-primary";

    /// <summary> CSS-класс контейнера. </summary>
    public string ContainerCssClass { get; set; } = string.Empty;
}

