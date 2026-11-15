using System.ComponentModel.DataAnnotations;

namespace OpenCRM.WebApp.Dtos.Admin.Organizations;

/// <summary> Причина изменения состояния организации администратором. </summary>
public sealed record ChangeAdminOrganizationArchiveRequest(
    [Required, StringLength(2_000, MinimumLength = 1)] string Reason);
