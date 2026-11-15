namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Подтверждённые сотрудником данные записи по рекомендации. </summary>
public sealed class CreateOrganizationRepeatVisitBookingDto
{
    public long Version { get; set; }
    public CreateOrganizationDetailingQuickBookingDto? Detailing { get; set; }
    public CreateOrganizationAutoServiceQuickBookingDto? AutoService { get; set; }
}
