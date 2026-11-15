using OpenCRM.Modules.Clients.Contracts.Dtos;

namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Данные выбранного сотрудником варианта конвертации обращения в плановую запись. </summary>
public sealed class CreateOrganizationInquiryBookingDto
{
    /// <summary> Версия обращения, показанная при открытии формы записи. </summary>
    public long Version { get; set; }

    /// <summary> Данные плановой записи детейлинга. </summary>
    public CreateOrganizationDetailingQuickBookingDto? Detailing { get; set; }

    /// <summary> Данные плановой приемки автосервиса. </summary>
    public CreateOrganizationAutoServiceQuickBookingDto? AutoService { get; set; }

    /// <summary> Явно введённые данные нового клиента, если сотрудник выбрал его создание. </summary>
    public CreateOrganizationClientDto? NewClient { get; set; }
}
