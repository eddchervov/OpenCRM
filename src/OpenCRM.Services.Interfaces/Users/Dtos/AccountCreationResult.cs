using OpenCRM.Domain.Identity;

namespace OpenCRM.Services.Interfaces.Users.Dtos;

/// <summary> Результат создания учётной записи. </summary>
public sealed record AccountCreationResult(ApplicationUser? User, IReadOnlyList<string> Errors);
