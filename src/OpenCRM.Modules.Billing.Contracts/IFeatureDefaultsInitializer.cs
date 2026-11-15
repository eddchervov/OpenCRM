namespace OpenCRM.Modules.Billing.Contracts;

/// <summary>Подготавливает системные определения функций и тарифов перед чтением.</summary>
public interface IFeatureDefaultsInitializer
{
    /// <summary> Создаёт системные определения функций и тарифов при необходимости. </summary>
    Task EnsureDefaultsAsync();
}
