using OpenCRM.Modules.Organizations.Contracts.Dtos;
using Helpers;
using OpenCRM.Services.Interfaces.Features.Dtos;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.WebApp.Models.Organizations;

namespace OpenCRM.WebApp.Factories;

/// <summary> Фабрика бокового меню рабочей области организации. </summary>
public static class OrganizationWorkspaceSidebarFactory
{
    /// <summary> Формирует боковое меню рабочей области по снимку доступа к функциям. </summary>
    public static OrganizationWorkspaceSidebarViewModel Build(
        OrganizationDto organization,
        FeatureAccessSnapshotDto workspaceFeatures,
        string currentSection,
        int? clientsCount = null,
        int? membersCount = null)
    {
        return Build(
            organization,
            currentSection,
            hasVehiclesModule: workspaceFeatures.Features.Any(x => x.Code == FeatureCodes.VehiclesModule && x.BoolValue == true),
            hasClientsModule: workspaceFeatures.Features.Any(x => x.Code == FeatureCodes.ClientsModule && x.BoolValue == true),
            hasDefectsModule: workspaceFeatures.Features.Any(x => x.Code == FeatureCodes.DefectsModule && x.BoolValue == true),
            hasCountersModule: workspaceFeatures.Features.Any(x => x.Code == FeatureCodes.CountersModule && x.BoolValue == true),
            hasSchedulerModule: workspaceFeatures.Features.Any(x => x.Code == FeatureCodes.SchedulerModule && x.BoolValue == true),
            clientsCount: clientsCount,
            membersCount: membersCount,
            hasMaterialsModule: workspaceFeatures.Features.Any(x => x.Code == FeatureCodes.MaterialsModule && x.BoolValue == true));
    }

    /// <summary> Формирует боковое меню рабочей области по списку доступных модулей. </summary>
    public static OrganizationWorkspaceSidebarViewModel Build(
        OrganizationDto organization,
        string currentSection,
        bool hasVehiclesModule,
        bool hasClientsModule,
        bool hasDefectsModule,
        bool hasCountersModule,
        bool hasSchedulerModule,
        int? clientsCount = null,
        int? membersCount = null,
        bool hasMaterialsModule = false)
    {
        return new OrganizationWorkspaceSidebarViewModel
        {
            OrganizationId = organization.Id,
            OrganizationName = organization.Name,
            PrimaryScenarioCode = organization.PrimaryScenarioCode,
            CurrentSection = currentSection,
            HasVehiclesModule = hasVehiclesModule,
            HasClientsModule = hasClientsModule,
            HasDefectsModule = hasDefectsModule,
            HasCountersModule = hasCountersModule,
            HasSchedulerModule = hasSchedulerModule,
            HasMaterialsModule = hasMaterialsModule,
            CanManageSettings = organization.CanManageSettings,
            ClientsCount = clientsCount,
            MembersCount = membersCount ?? organization.MembersCount
        };
    }
}
