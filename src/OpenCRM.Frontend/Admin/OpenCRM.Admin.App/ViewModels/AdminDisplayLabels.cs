using OpenCRM.Admin.App.Localization;
using OpenCRM.Admin.Api;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Локализует встроенные подписи по кодам, сохраняя неизвестные значения. </summary>
internal static class AdminDisplayLabels
{
    public static string? TransportErrorKey(ApiException exception) => exception.StatusCode != null ? null
        : exception.InnerException switch
        {
            HttpRequestException => "native.roles.ne.udalos.svyazatsya.s.serverom.proverte.podklyuchenie.i.povtori",
            OperationCanceledException => "native.roles.prevysheno.vremya.ozhidaniya.otveta.servera.povtorite.popytku",
            _ => null
        };

    public static string OrganizationRole(string code, string name) => code.ToLowerInvariant() switch
    {
        "owner" => AdminDisplayLabels.Localize("userCard.organization.role.owner"),
        "admin" => AdminDisplayLabels.Localize("userCard.organization.role.admin"),
        "employee" => AdminDisplayLabels.Localize("userCard.organization.role.employee"),
        "performer" => AdminDisplayLabels.Localize("userCard.organization.role.performer"),
        "viewer" => AdminDisplayLabels.Localize("userCard.organization.role.viewer"),
        _ => UserDisplayFormatting.Text(name)
    };

    public static string OrganizationScenario(string code, string name) => code.ToLowerInvariant() switch
    {
        "personal" => AdminDisplayLabels.Localize("userCard.organization.scenario.personal"),
        "service-station" => AdminDisplayLabels.Localize("userCard.organization.scenario.serviceStation"),
        "detailing" => AdminDisplayLabels.Localize("userCard.organization.scenario.detailing"),
        "wrapping" => AdminDisplayLabels.Localize("userCard.organization.scenario.wrapping"),
        "car-wash" => AdminDisplayLabels.Localize("userCard.organization.scenario.carWash"),
        "fleet" => AdminDisplayLabels.Localize("userCard.organization.scenario.fleet"),
        "driving-school" => AdminDisplayLabels.Localize("userCard.organization.scenario.drivingSchool"),
        _ => UserDisplayFormatting.Text(name)
    };

    public static string MaintenanceStatus(int code, string name) => code switch
    {
        1 => AdminDisplayLabels.Localize("userCard.maintenance.status.planned"),
        2 => AdminDisplayLabels.Localize("userCard.maintenance.status.inProgress"),
        3 => AdminDisplayLabels.Localize("userCard.maintenance.status.completed"),
        4 => AdminDisplayLabels.Localize("userCard.maintenance.status.cancelled"),
        _ => UserDisplayFormatting.Text(name)
    };

    public static string MaintenanceType(int code, string name) => code switch
    {
        1 => AdminDisplayLabels.Localize("userCard.maintenance.type.maintenance"),
        2 => AdminDisplayLabels.Localize("userCard.maintenance.type.repair"),
        3 => AdminDisplayLabels.Localize("userCard.maintenance.type.refill"),
        100 => AdminDisplayLabels.Localize("userCard.maintenance.type.other"),
        _ => UserDisplayFormatting.Text(name)
    };

    public static string VehicleKind(int code, string name) => code switch
    {
        1 => AdminDisplayLabels.Localize("userCard.vehicle.kind.passengerCar"),
        2 => AdminDisplayLabels.Localize("userCard.vehicle.kind.truck"),
        3 => AdminDisplayLabels.Localize("userCard.vehicle.kind.motorcycle"),
        4 => AdminDisplayLabels.Localize("userCard.vehicle.kind.atv"),
        5 => AdminDisplayLabels.Localize("userCard.vehicle.kind.utv"),
        6 => AdminDisplayLabels.Localize("userCard.vehicle.kind.snowmobile"),
        7 => AdminDisplayLabels.Localize("userCard.vehicle.kind.jetSki"),
        8 => AdminDisplayLabels.Localize("userCard.vehicle.kind.boat"),
        9 => AdminDisplayLabels.Localize("userCard.vehicle.kind.specialEquipment"),
        10 => AdminDisplayLabels.Localize("userCard.vehicle.kind.trailer"),
        99 => AdminDisplayLabels.Localize("userCard.vehicle.kind.other"),
        _ => UserDisplayFormatting.Text(name)
    };

    public static string MileageUnit(int code, string name) => code switch
    {
        1 => AdminDisplayLabels.Localize("userCard.vehicle.mileageUnit.kilometers"),
        2 => AdminDisplayLabels.Localize("userCard.vehicle.mileageUnit.engineHours"),
        _ => UserDisplayFormatting.Text(name)
    };

    public static string SupportType(string code, string name) => code switch
    {
        "question" => AdminDisplayLabels.Localize("userCard.support.type.question"),
        "suggestion" => AdminDisplayLabels.Localize("userCard.support.type.suggestion"),
        "bug" => AdminDisplayLabels.Localize("userCard.support.type.bug"),
        "billing" => AdminDisplayLabels.Localize("userCard.support.type.billing"),
        "cooperation" => AdminDisplayLabels.Localize("userCard.support.type.cooperation"),
        _ => UserDisplayFormatting.Text(name)
    };

    public static string SupportStatus(string code, string name) => code switch
    {
        "new" => AdminDisplayLabels.Localize("userCard.support.status.new"),
        "in_progress" => AdminDisplayLabels.Localize("userCard.support.status.inProgress"),
        "closed" => AdminDisplayLabels.Localize("userCard.support.status.closed"),
        "spam" => AdminDisplayLabels.Localize("userCard.support.status.spam"),
        _ => UserDisplayFormatting.Text(name)
    };

    public static string PaymentStatus(string code, string name, string? applicationStatus = null) => applicationStatus == "needs_attention"
        ? AdminDisplayLabels.Localize("core.payment.needsAttention")
        : code.Trim().ToLowerInvariant() switch
    {
        "" => AdminDisplayLabels.Localize("userCard.payment.status.noStatus"),
        "completed" => AdminDisplayLabels.Localize("userCard.payment.status.completed"),
        "succeeded" => AdminDisplayLabels.Localize("userCard.payment.status.succeeded"),
        "pending" => AdminDisplayLabels.Localize("userCard.payment.status.pending"),
        "processing" => AdminDisplayLabels.Localize("userCard.payment.status.processing"),
        "waiting_for_capture" => AdminDisplayLabels.Localize("userCard.payment.status.waitingForCapture"),
        "paid_waiting_confirmation" => AdminDisplayLabels.Localize("userCard.payment.status.paidWaitingConfirmation"),
        "canceled" => AdminDisplayLabels.Localize("userCard.payment.status.canceled"),
        "failed" => AdminDisplayLabels.Localize("userCard.payment.status.failed"),
        "refunded" => AdminDisplayLabels.Localize("userCard.payment.status.refunded"),
        _ => UserDisplayFormatting.Text(name)
    };

    public static string PaymentMethod(string code, string name) => code.Trim().ToLowerInvariant() switch
    {
        "" => AdminDisplayLabels.Localize("userCard.payment.method.noMethod"),
        "yookassa" => "YooKassa",
        "yookassa_purchase" => AdminDisplayLabels.Localize("userCard.payment.method.yooKassaPurchase"),
        "yookassa_extension" => AdminDisplayLabels.Localize("userCard.payment.method.yooKassaExtension"),
        var value when value.StartsWith("yookassa_workspace_purchase:", StringComparison.Ordinal) => AdminDisplayLabels.Localize("userCard.payment.method.yooKassaWorkspacePurchase"),
        var value when value.StartsWith("yookassa_workspace_extension:", StringComparison.Ordinal) => AdminDisplayLabels.Localize("userCard.payment.method.yooKassaWorkspaceExtension"),
        var value when value.StartsWith("workspace_purchase:", StringComparison.Ordinal) => AdminDisplayLabels.Localize("userCard.payment.method.workspacePurchase"),
        var value when value.StartsWith("workspace_extension:", StringComparison.Ordinal) => AdminDisplayLabels.Localize("userCard.payment.method.workspaceExtension"),
        _ => UserDisplayFormatting.Text(name)
    };

    private static string Localize(string key) => AdminLocalization.Instance[key];
}
