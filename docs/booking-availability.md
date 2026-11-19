# Bay availability for appointments

**English** | [Русский](booking-availability.ru.md) | [简体中文](booking-availability.zh-CN.md)

The shared `BookingTimeFields` component is used for quick Detailing and Service Station appointments and in the shared maintenance create/edit form.

- After a date and bay are selected, occupied intervals are loaded. A previous request is cancelled when parameters change.
- A start time inside an occupied interval is unavailable. An hour is blocked in full only when every minute in it is unavailable at the current selection interval.
- The end time is checked against the entire interval from the selected start. An appointment ending at 11:00 does not prevent the next one from starting at 11:00.
- A hint shows the occupied interval, license plate, and service. On mobile, an expandable availability list is available.
- Before saving, manually entered and already selected intervals are checked after availability is reloaded. A loading error is not treated as an available bay.
- While editing, the current appointment is excluded. Cancelled and deleted appointments do not occupy a bay.
- The server-side check on save remains mandatory. The interface does not replace it.

## API

`GET /api/v1/maintenance/occupied-slots?organizationId=…&date=2026-09-11&location=Пост%201&excludeMaintenanceId=…`

Authentication and the organization's `MaintenanceView` permission are required. The excluded appointment ID is optional. The response is not cached or limited by the calendar page size; it contains `id`, `startMinute`, `endMinute`, `vehicle`, and `service`. Minutes are counted from the start of the planned date.

Availability loading and conflict validation use the `OpenCRM.Modules.Maintenance` module rules. Scheduling and availability use the planned date regardless of status. The actual completion date is used in history and reports and does not move the appointment to another schedule day. For legacy appointments without a planned date, both requests use the main appointment date. Bay names are compared case-insensitively after trimming surrounding whitespace. If the end is missing, the existing rule applies: one hour after the start.

Multi-day availability and the full API date range are described in [the period rules](maintenance-periods.md).

## Statuses and dates

| Status | Available transitions | Completion date |
| --- | --- | --- |
| Planned | Start work, Complete, Cancel | Hidden and not saved |
| In progress | Complete, Cancel, Return to planned | Hidden and not saved |
| Completed | Return to work with confirmation, edit data | Shown and editable |
| Cancelled | Resume appointment with date selection | Hidden and not saved |

In the normal workflow, a new appointment is created as planned. An existing appointment is completed using the “Complete” action on its card: the actual date is recorded and the planned date remains unchanged. The editor does not offer unsupported transitions; the server also prevents completion and reverse transitions through an arbitrary status change in the editor. Reverse transitions use separate card actions. Resuming a cancelled appointment checks bay availability again.

The simplified Service Station mode allows work to be created as completed. Editing preserves its original status and separate actual date; it does not automatically complete a cancelled or active appointment or replace the actual date with the planned date.

Photos, services, materials, and calculations remain part of history, including for cancelled appointments. Work completion documents are available only for completed work.

## Verification

`OccupiedMaintenanceSlotsTests` checks PostgreSQL queries for organization, date, bay, status, exclusion of the edited appointment, no truncation at 500 records, and authorization before reading data.

Additional checks cover calendar/availability consistency when planned and actual dates differ, rejection of overlaps during creation and editing, valid status transitions, and recording the date through the “Complete” action. Browser scenarios cover all four statuses in three clients and simplified Service Station mode.

## Card actions and reverse transitions

The next step is highlighted in the card header: “Start work” for a planned appointment, “Complete” for one in progress, and “Resume appointment” for a cancelled one. Other operations are available through “Actions”. A state change requires confirmation; if it fails, the dialog stays open and shows the server response. A comment can be added to history when cancelling or returning. Rescheduling and resuming prompt for a date and time and check availability.

- `return_to_planned`: only from “In progress”; keeps the planned date and time and clears the actual date.
- `return_to_work`: only from “Completed”; returns the appointment to work and clears the actual date. Its effect on current mileage is removed and recalculated from the remaining readings.
- Both commands check bay conflicts, preserve services, photos, payments, and materials, and are recorded in history.
- Linked defects retain their independent status: returning work does not make an already resolved defect reappear. The confirmation dialog states this explicitly when links exist.
- The API uses the existing `IApplicationUnitOfWork`: state, history, mileage, and scheduler-task synchronization are saved in one transaction. The appointment row is locked until the action completes; after acquiring the lock, its status is re-read. Repeating a reverse command does not create a second event.

The separate `MaintenanceServiceCatalog` class was removed. Service composition validation is in `OpenCRM.Modules.Maintenance`; quick appointment creation obtains an active service through `IOrganizationScenarioCatalogService.GetActiveServiceAsync`. The new card interface is in the shared `MaintenanceCardActions.vue` component; the existing compact list action component remains.
