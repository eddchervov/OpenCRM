using System.ComponentModel.DataAnnotations;

namespace OpenCRM.WebApp.Models.Organizations;

/// <summary>
/// DTO применения организационного промокода.
/// </summary>
public class ApplyOrganizationPromoCodeDto
{
    /// <summary> Промокод, применяемый к организации. </summary>
    [Required]
    [StringLength(64, MinimumLength = 3)]
    public string Code { get; set; } = string.Empty;
}
