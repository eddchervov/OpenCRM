export { default as WorkspaceCrmShell } from './components/WorkspaceCrmShell.vue'
export { default as ClientsSection } from './components/ClientsSection.vue'
export { provideWorkspaceCurrency, useWorkspaceCurrency } from './workspaceCurrency'
import './localization'
export { default as ClientCardSection } from './components/ClientCardSection.vue'
export { default as MaintenanceCardSection } from './components/MaintenanceCardSection.vue'
export { default as MaintenanceFormSection } from './components/MaintenanceFormSection.vue'
export { default as MaintenanceWorkflowActions } from './components/MaintenanceWorkflowActions.vue'
export { default as VehicleCardSection } from './components/VehicleCardSection.vue'
export { default as VehicleFormSection } from './components/VehicleFormSection.vue'
export { default as VehicleRegistrationPlateInput } from './components/VehicleRegistrationPlateInput.vue'
export {
  createVehicleRegistrationPlateMask,
  getVehicleRegistrationPlateFormat,
  loadVehicleRegistrationPlateFormats,
  normalizeRegistrationPlate,
  prepareRegistrationPlatePaste,
  validateRegistrationPlate
} from './vehicleRegistrationPlate'
export type { VehicleRegistrationPlateFormat, VehicleRegistrationPlateFormats } from './vehicleRegistrationPlate'
export { default as VehicleMediaSection } from './components/VehicleMediaSection.vue'
export { default as VehiclesSection } from './components/VehiclesSection.vue'
export { default as WorkspaceBreadcrumbs } from './components/WorkspaceBreadcrumbs.vue'
export { workspaceLabelKey, workspaceGroupKey } from './workspaceChromeMessages'
export { default as WorkspaceAccessNotice } from './components/WorkspaceAccessNotice.vue'
export { default as WorkspacePageHelpButton } from './components/WorkspacePageHelpButton.vue'
export { default as WorkspaceCommonSection } from './components/WorkspaceCommonSection.vue'
export { createRouteScopedNotice, getWorkspaceRouteKey } from './routeScopedNotice'
export { useSchedulerViewState } from './schedulerViewState'
export { default as FinanceSection } from './components/FinanceSection.vue'
export { default as Export1CSection } from './components/Export1CSection.vue'
export {
  buildWorkspaceCommonSections,
  isWorkspaceCommonSectionCode,
  resolveCommonSectionHref,
  resolveWorkspaceWikiHref,
  resolveWorkspaceWikiSlug,
  workspaceCommonSectionCodes
} from './commonSections'
export {
  createMaintenance,
  deleteMaintenancePhoto,
  loadMaintenance,
  loadMaintenancePhotos,
  changeMaintenanceWorkflow,
  updateMaintenance,
  uploadMaintenancePhotos
} from './api/maintenance'
export { createClient, loadClientDetails, loadClientOptions, loadClientsPage, updateClient } from './api/clients'
export { loadScenarioCatalog } from './api/scenarioCatalog'
export {
  loadWorkspaceBilling,
  loadWorkspaceMembers,
  loadWorkspaceOverview,
  loadWorkspaceSettings,
  loadWorkspaceStatistics,
  loadWorkspaceWiki,
  updateWorkspaceProfile,
  updateWorkspaceRequisites,
  inviteWorkspaceMember,
  updateWorkspaceMemberRole,
  removeWorkspaceMember,
  transferWorkspaceOwnership,
  revokeWorkspaceInvitation,
  updateWorkspaceRolePermissions,
  activateWorkspaceDemoPlan,
  purchaseWorkspaceBusinessPlan,
  extendWorkspaceBusinessPlan
} from './api/workspaceAdmin'
export { loadWorkspaceFinance } from './api/finance'
export {
  createVehicle,
  createVehicleAlbum,
  deleteVehicleAlbum,
  deleteVehicleAlbumPhoto,
  deleteVehiclePhoto,
  loadVehicle,
  loadVehicleAlbums,
  loadVehicleHistory,
  loadVehicleBrandOptions,
  loadVehicleMaintenancePage,
  loadVehicleModelOptions,
  loadVehiclePhotoList,
  loadVehiclePhotos,
  loadVehiclesPage,
  setPrimaryVehicleAlbumPhoto,
  setPrimaryVehiclePhoto,
  updateVehicle,
  uploadVehicleAlbumPhotos,
  uploadVehiclePhotos
} from './api/vehicles'
export type {
  WorkspaceClient,
  WorkspaceClientDefectItem,
  WorkspaceClientDetails,
  WorkspaceClientHistoryEvent,
  WorkspaceClientMaintenanceItem,
  WorkspaceClientOption,
  WorkspaceClientSaveRequest,
  WorkspaceClientSegment,
  WorkspaceClientsPage,
  WorkspaceClientTag,
  WorkspaceClientVehicle
} from './api/clients'
export type {
  WorkspaceMaintenance,
  WorkspaceMaintenanceDefect,
  WorkspaceMaintenanceDetail,
  WorkspaceMaintenancePhoto,
  WorkspaceMaintenanceSaveRequest,
  WorkspaceMaintenanceWorkflowAction
} from './api/maintenance'
export type {
  WorkspaceScenarioCatalog,
  WorkspaceScenarioCatalogItem
} from './api/scenarioCatalog'
export type {
  WorkspaceBillingData,
  WorkspaceFeature,
  WorkspaceMembersData,
  WorkspaceOrganization,
  WorkspaceOrganizationInvitation,
  WorkspaceOrganizationMember,
  WorkspaceOverviewData,
  WorkspacePlan,
  WorkspaceProfileSaveRequest,
  WorkspaceRequisitesSaveRequest,
  WorkspaceActionResult,
  WorkspaceRolePermission,
  WorkspaceRolePermissionMatrix,
  WorkspaceSettingsData,
  WorkspaceStatisticsData,
  WorkspaceSubscription,
  WorkspaceWikiArticle,
  WorkspaceWikiData
} from './api/workspaceAdmin'
export type {
  WorkspaceFinanceBreakdownItem,
  WorkspaceFinanceClientDebtItem,
  WorkspaceFinanceKpis,
  WorkspaceFinanceMoneySeriesPoint,
  WorkspaceFinanceOrderItem,
  WorkspaceFinanceServiceItem,
  WorkspaceFinanceSummary
} from './api/finance'
export type { WorkspaceBreadcrumbItem } from './components/WorkspaceBreadcrumbs.vue'
export type { WorkspaceCommonSectionCode, WorkspaceCommonSectionItem } from './commonSections'
export { isDiscreteMaterialUnit, materialUnitOptions, normalizeMaterialUnit } from './materialUnits'
export type {
  WorkspaceVehicle,
  WorkspaceVehicleAlbum,
  WorkspaceVehicleAlbumsResponse,
  WorkspaceVehicleBrandFilter,
  WorkspaceVehicleCatalogOption,
  WorkspaceVehicleHistoryEvent,
  WorkspaceVehicleMaintenancePage,
  WorkspaceVehiclePhoto,
  WorkspaceVehicleSaveRequest,
  WorkspaceVehiclesPage
} from './api/vehicles'
export type {
  WorkspaceContext,
  WorkspaceMenuItem,
  WorkspaceOrganizationSwitcherItem,
  WorkspaceQuickAction
} from './types'

export { trackWorkspaceGoal } from './analytics'
export { default as SchedulerToolbar } from './components/SchedulerToolbar.vue'

export { default as BookingTimeFields } from './components/BookingTimeFields.vue'

export { default as SchedulerList } from './components/SchedulerList.vue'

export { getVehicleDisplayName } from './api/vehicles'

export { bookingDayOffset } from './api/bookingSlots'

export { default as MaintenanceServicesEditor } from './components/MaintenanceServicesEditor.vue'
export { default as WorkspaceWorkdayBoard } from './components/WorkspaceWorkdayBoard.vue'
export { loadWorkspaceWorkday } from './api/workday'
export type { WorkspaceWorkday, WorkspaceWorkdayGroup, WorkspaceWorkdayItem } from './api/workday'

export { loadWorkspaceSetup } from './api/setup'
export type { WorkspaceWorkingDay, WorkspaceWorkingDayBreak, WorkspaceSetup } from './api/setup'
export { hasWorkingSchedule, scheduleBusinessHours, workingIntervals } from './workingHours'

export { default as SchedulerDayCalendar } from './components/SchedulerDayCalendar.vue'
export { default as SchedulerBookingDialog } from './components/SchedulerBookingDialog.vue'
export { getVehicleBrandLogo } from './utils/vehicleBrandLogo'
export { getPathBase, pushHashState, withPathBase } from './utils/pathBase.mjs'
export { formatServiceDuration, getUnknownServiceDurationSaveMessage } from './serviceDuration'

export { default as SchedulerSlots } from './components/SchedulerSlots.vue'
export { default as SchedulerClosedDay } from './components/SchedulerClosedDay.vue'
export { default as SchedulerDayRecords } from './components/SchedulerDayRecords.vue'
export { default as SchedulerRecordPreview } from './components/SchedulerRecordPreview.vue'
export { getSchedulerDayInterval, getClosedSchedulerPosts } from './utils/schedulerDay'

export { renderSchedulerCalendarEvent, schedulerCalendarStatus } from './utils/schedulerCalendarEvent'
export { createMaterialNumberMask } from './materialNumberMask'
export { default as MaintenanceJournalSection } from './components/MaintenanceJournalSection.vue'
export { isSimpleMaintenanceWorkflow, supportsMaintenanceWorkflow } from './utils/maintenanceWorkflow'

export { loadInquiry, convertInquiryBooking, type Inquiry } from './api/inquiries'
export { loadRepeatVisit, convertRepeatVisitBooking, type RepeatVisit } from './api/repeatVisits'
export { default as InquiryBookingContext } from './components/InquiryBookingContext.vue'
export { inquiryUtcTime as organizationUtcTime } from './utils/inquiryTime'

export { default as QuickBookingClientPicker } from './components/QuickBookingClientPicker.vue'

export { registrationCountryOptions } from './utils/registrationCountries'
export { internationalRegistrationPlateError } from './internationalRegistrationPlate'
