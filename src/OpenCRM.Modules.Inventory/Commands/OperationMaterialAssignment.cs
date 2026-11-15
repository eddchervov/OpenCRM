using OpenCRM.Domain;
using OpenCRM.Domain.Identity;

namespace OpenCRM.Modules.Inventory.Commands;

/// <summary> Данные записи обслуживания и сотрудника для выдачи материалов. </summary>
internal sealed record OperationMaterialAssignment(OpenCRM.Domain.Maintenance Maintenance, OrganizationMember Member, OrganizationEmployee Employee);
